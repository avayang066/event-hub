using System.Net;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>報名、名額控制、訂單頁面。</summary>
public class RegistrationTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private static string RegisterUrl(int eventId) => $"/Events/{eventId}/Register";

    // ---------------- 準備資料 ----------------

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>註冊並登入一個帳號，回傳 client 與使用者 Id。</summary>
    private async Task<(HttpClient Client, string UserId)> SignUpAsync(string accountType = "Attendee")
    {
        var client = NewClient();
        var email = TestClientExtensions.NewEmail();
        await client.RegisterAsync(email, accountType);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return (client, user!.Id);
    }

    private async Task<string> CreateOrganizerAsync()
    {
        var (_, id) = await SignUpAsync("Organizer");
        return id;
    }

    private Task<int> SeedEventAsync(
        string organizerId,
        EventStatus status = EventStatus.Published,
        int price = 1000,
        int capacity = 10,
        int reservedCount = 0,
        DateTime? startAt = null,
        DateTime? registrationDeadline = null) =>
        factory.WithDbAsync(async db =>
        {
            var start = startAt ?? DateTime.Now.AddDays(30);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = "報名測試活動",
                StartAt = start,
                EndAt = start.AddHours(2),
                RegistrationDeadline = registrationDeadline,
                Location = "台北",
                Price = price,
                Capacity = capacity,
                ReservedCount = reservedCount,
                Status = status,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
            };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

    private Task<Event> GetEventAsync(int id) =>
        factory.WithDbAsync(db => db.Events.AsNoTracking().SingleAsync(e => e.Id == id));

    private Task<List<Order>> GetOrdersAsync(int eventId) =>
        factory.WithDbAsync(db => db.Orders.AsNoTracking().Where(o => o.EventId == eventId).ToListAsync());

    private static Dictionary<string, string> RegistrationForm(Dictionary<string, string>? overrides = null)
    {
        var form = new Dictionary<string, string>
        {
            ["AttendeeName"] = "王小明",
            ["AttendeePhone"] = "0912-345-678",
            ["AttendeeEmail"] = "ming@test.local",
            ["Note"] = "素食",
        };
        foreach (var (key, value) in overrides ?? new())
        {
            form[key] = value;
        }
        return form;
    }

    /// <summary>
    /// 直接送出報名表。防偽權杖從活動頁取得（登入後頁首有登出表單），
    /// 不依賴報名表單頁，才能測到「表單頁會擋掉、但有人直接 POST」的情況。
    /// </summary>
    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, int eventId, Dictionary<string, string>? form = null) =>
        client.PostFormAsync($"/Events/{eventId}", RegisterUrl(eventId), form ?? RegistrationForm());

    /// <summary>台灣時間（與網站使用的時間一致，不受測試機器時區影響）。</summary>
    private static DateTime TaiwanNow => DateTime.UtcNow.AddHours(8);

    private static string OrderNoFromRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var path = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/Orders/", path);
        return path["/Orders/".Length..];
    }

    // ---------------- 權限 ----------------

    [Fact]
    public async Task Anonymous_visitor_is_sent_to_login_then_back_to_register_form()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());

        var response = await NewClient().GetAsync(RegisterUrl(eventId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("/Account/Login", location);
        Assert.Contains(Uri.EscapeDataString(RegisterUrl(eventId)), location);
    }

    [Fact]
    public async Task Organizer_cannot_register()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (organizerClient, _) = await SignUpAsync("Organizer");

        var response = await organizerClient.GetAsync(RegisterUrl(eventId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
        Assert.Contains("僅限報名者帳號報名", await organizerClient.GetStringAsync($"/Events/{eventId}"));
    }

    [Fact]
    public async Task Draft_event_cannot_be_registered()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), EventStatus.Draft);
        var (client, _) = await SignUpAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(RegisterUrl(eventId))).StatusCode);
    }

    // ---------------- 報名成功 ----------------

    [Fact]
    public async Task Register_paid_event_creates_pending_order_and_reserves_seat()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), price: 1000, reservedCount: 2);
        var (client, userId) = await SignUpAsync();
        var before = TaiwanNow;

        var orderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        var order = (await GetOrdersAsync(eventId)).Single();
        Assert.Equal(orderNo, order.OrderNo);
        Assert.Equal(20, order.OrderNo.Length);
        Assert.StartsWith("EH", order.OrderNo);
        Assert.Equal(userId, order.UserId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1000, order.Amount);
        Assert.Equal(50, order.PlatformFee);           // 5%
        Assert.Equal(950, order.OrganizerAmount);
        Assert.Equal("0912345678", order.AttendeePhone); // 去掉 -
        Assert.Null(order.PaidAt);
        Assert.NotNull(order.ExpiresAt);
        Assert.InRange(order.ExpiresAt!.Value, before.AddMinutes(14), before.AddMinutes(16));
        Assert.Equal(3, (await GetEventAsync(eventId)).ReservedCount);

        var page = await client.GetStringAsync($"/Orders/{orderNo}");
        Assert.Contains("待付款", page);
        Assert.Contains(orderNo, page);
    }

    [Fact]
    public async Task Register_free_event_is_paid_immediately()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), price: 0);
        var (client, _) = await SignUpAsync();

        var orderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        var order = (await GetOrdersAsync(eventId)).Single();
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.PaidAt);
        Assert.Null(order.ExpiresAt);
        Assert.Equal(0, order.PlatformFee);
        Assert.Contains("報名成功", await client.GetStringAsync($"/Orders/{orderNo}"));
    }

    [Fact]
    public async Task Event_page_shows_my_registration_after_registering()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (client, _) = await SignUpAsync();
        await SubmitAsync(client, eventId);

        var html = await client.GetStringAsync($"/Events/{eventId}");

        Assert.Contains("查看我的報名", html);
    }

    // ---------------- 不能報名的情況 ----------------

    [Fact]
    public async Task Same_user_cannot_register_twice()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (client, _) = await SignUpAsync();
        var firstOrderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        // 直接 POST（略過表單頁的檢查），模擬重複送出
        var second = await SubmitAsync(client, eventId);

        Assert.Equal(firstOrderNo, OrderNoFromRedirect(second));
        Assert.Single(await GetOrdersAsync(eventId));
        Assert.Equal(1, (await GetEventAsync(eventId)).ReservedCount);
    }

    [Fact]
    public async Task Opening_register_form_when_already_registered_redirects_to_order()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (client, _) = await SignUpAsync();
        var orderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        var response = await client.GetAsync(RegisterUrl(eventId));

        Assert.Equal(orderNo, OrderNoFromRedirect(response));
    }

    [Fact]
    public async Task Full_event_rejects_registration()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), capacity: 2, reservedCount: 2);
        var (client, _) = await SignUpAsync();

        var response = await SubmitAsync(client, eventId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("名額已滿", await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetOrdersAsync(eventId));
        Assert.Equal(2, (await GetEventAsync(eventId)).ReservedCount);
    }

    [Fact]
    public async Task Registration_after_deadline_is_rejected()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(),
            registrationDeadline: DateTime.Now.AddMinutes(-5));
        var (client, _) = await SignUpAsync();

        var response = await client.GetAsync(RegisterUrl(eventId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("報名已截止", await client.GetStringAsync($"/Events/{eventId}"));
        Assert.Empty(await GetOrdersAsync(eventId));
    }

    [Fact]
    public async Task Cancelled_event_rejects_registration()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), EventStatus.Cancelled);
        var (client, _) = await SignUpAsync();

        var response = await SubmitAsync(client, eventId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("活動已取消", await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetOrdersAsync(eventId));
    }

    [Theory]
    [InlineData("AttendeePhone", "12345", "請輸入手機號碼，例如 0912345678")]
    [InlineData("AttendeeEmail", "not-an-email", "Email 格式不正確")]
    [InlineData("AttendeeName", "", "請輸入姓名")]
    public async Task Form_fields_are_validated(string field, string value, string expectedError)
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (client, _) = await SignUpAsync();

        var response = await SubmitAsync(client, eventId, RegistrationForm(new() { [field] = value }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(
            $"field-validation-error\"[^>]*data-valmsg-for=\"{field}\"[^>]*>{System.Text.RegularExpressions.Regex.Escape(expectedError)}",
            await response.Content.ReadAsStringAsync());
        Assert.Empty(await GetOrdersAsync(eventId));
    }

    [Fact]
    public async Task Expired_pending_order_is_released_when_user_registers_again()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), capacity: 1);
        var (client, _) = await SignUpAsync();
        var oldOrderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        // 模擬超過付款期限
        await factory.WithDbAsync(db => db.Orders
            .Where(o => o.OrderNo == oldOrderNo)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, TaiwanNow.AddMinutes(-1))));

        var newOrderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        Assert.NotEqual(oldOrderNo, newOrderNo);
        var orders = await GetOrdersAsync(eventId);
        Assert.Equal(OrderStatus.Cancelled, orders.Single(o => o.OrderNo == oldOrderNo).Status);
        Assert.Equal(OrderStatus.Pending, orders.Single(o => o.OrderNo == newOrderNo).Status);
        // 舊的釋放 1、新的佔 1，名額 1 的活動仍可報名成功
        Assert.Equal(1, (await GetEventAsync(eventId)).ReservedCount);
    }

    // ---------------- 同時搶名額 ----------------

    [Fact]
    public async Task Concurrent_registrations_never_exceed_capacity()
    {
        const int capacity = 3;
        const int attendees = 10;
        var eventId = await SeedEventAsync(await CreateOrganizerAsync(), capacity: capacity);

        // 先讓 10 個人都登入、打開報名表（取得防偽權杖）
        var prepared = new List<(HttpClient Client, FormUrlEncodedContent Content)>();
        for (var i = 0; i < attendees; i++)
        {
            var (client, _) = await SignUpAsync();
            var token = await client.GetAntiForgeryTokenAsync(RegisterUrl(eventId));
            var form = new Dictionary<string, string>(RegistrationForm()) { ["__RequestVerificationToken"] = token };
            prepared.Add((client, new FormUrlEncodedContent(form)));
        }

        // 10 個人同時按下送出
        var responses = await Task.WhenAll(
            prepared.Select(p => p.Client.PostAsync(RegisterUrl(eventId), p.Content)));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Redirect);
        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();

        Assert.Equal(capacity, succeeded);
        Assert.Equal(attendees - capacity, rejected.Count);
        foreach (var r in rejected)
        {
            Assert.Contains("名額已滿", await r.Content.ReadAsStringAsync());
        }

        Assert.Equal(capacity, (await GetEventAsync(eventId)).ReservedCount);
        Assert.Equal(capacity, (await GetOrdersAsync(eventId)).Count);
    }

    // ---------------- 訂單頁 ----------------

    [Fact]
    public async Task Users_cannot_see_other_peoples_orders()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (owner, _) = await SignUpAsync();
        var orderNo = OrderNoFromRedirect(await SubmitAsync(owner, eventId));
        var (stranger, _) = await SignUpAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/Orders/{orderNo}")).StatusCode);
        Assert.DoesNotContain(orderNo, await stranger.GetStringAsync("/Orders"));
    }

    [Fact]
    public async Task My_registrations_lists_own_orders()
    {
        var eventId = await SeedEventAsync(await CreateOrganizerAsync());
        var (client, _) = await SignUpAsync();
        var orderNo = OrderNoFromRedirect(await SubmitAsync(client, eventId));

        var html = await client.GetStringAsync("/Orders");

        Assert.Contains(orderNo, html);
        Assert.Contains("報名測試活動", html);
        Assert.Contains("待付款", html);
    }
}
