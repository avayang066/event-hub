using System.Net;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>訂單狀態轉換：付款、取消（本人／逾期／活動取消）、退款。</summary>
public class OrderStateTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private static DateTime TaiwanNow => DateTime.UtcNow.AddHours(8);

    /// <summary>綠界交易編號有唯一索引，每次產生不同的。</summary>
    private static string NewTradeNo() => Guid.NewGuid().ToString("N")[..20];

    // ---------------- 準備資料 ----------------

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>直接建立帳號（不經過註冊頁），回傳 Id 與 Email。</summary>
    private async Task<(string Id, string Email)> CreateUserAsync(string role)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = TestClientExtensions.NewEmail();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = role };
        await userManager.CreateAsync(user, TestClientExtensions.DefaultPassword);
        await userManager.AddToRoleAsync(user, role);
        return (user.Id, email);
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = NewClient();
        await client.LoginAsync(email);
        return client;
    }

    private Task<int> SeedEventAsync(string organizerId, int capacity = 10, int reservedCount = 0, int price = 1000) =>
        factory.WithDbAsync(async db =>
        {
            var start = TaiwanNow.AddDays(30);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = "狀態測試活動",
                StartAt = start,
                EndAt = start.AddHours(2),
                Location = "台北",
                Price = price,
                Capacity = capacity,
                ReservedCount = reservedCount,
                Status = EventStatus.Published,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

    /// <summary>
    /// 直接建立訂單（不會自動增加活動的 reserved_count，請在 SeedEventAsync 指定一致的數量）。
    /// </summary>
    private Task<string> SeedOrderAsync(
        int eventId, string userId, string organizerId, OrderStatus status,
        DateTime? expiresAt = null, int amount = 1000) =>
        factory.WithDbAsync(async db =>
        {
            var order = new Order
            {
                OrderNo = "EH" + Guid.NewGuid().ToString("N")[..18].ToUpperInvariant(),
                EventId = eventId,
                UserId = userId,
                OrganizerId = organizerId,
                AttendeeName = "測試",
                AttendeePhone = "0912345678",
                AttendeeEmail = "a@test.local",
                Amount = amount,
                PlatformFee = amount / 20,
                OrganizerAmount = amount - amount / 20,
                Status = status,
                ExpiresAt = status == OrderStatus.Pending ? expiresAt ?? TaiwanNow.AddMinutes(15) : null,
                PaidAt = status == OrderStatus.Paid ? TaiwanNow : null,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.OrderNo;
        });

    private Task<Order> GetOrderAsync(string orderNo) =>
        factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.OrderNo == orderNo));

    private Task<int> GetReservedCountAsync(int eventId) =>
        factory.WithDbAsync(db => db.Events.Where(e => e.Id == eventId).Select(e => e.ReservedCount).SingleAsync());

    private async Task<T> WithServiceAsync<T>(Func<OrderStateService, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<OrderStateService>());
    }

    // ---------------- 待付款 → 已付款 ----------------

    [Fact]
    public async Task MarkPaid_changes_pending_order_to_paid()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Pending);

        var outcome = await WithServiceAsync(s => s.MarkPaidAsync(orderNo, "2611041530123456", "Credit_CreditCard"));

        Assert.Equal(OrderStateService.MarkPaidOutcome.Paid, outcome);
        var order = await GetOrderAsync(orderNo);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.PaidAt);
        Assert.Equal("2611041530123456", order.EcpayTradeNo);
        Assert.Equal("Credit_CreditCard", order.PaymentType);
        Assert.Equal(1, await GetReservedCountAsync(eventId)); // 付款不影響名額
    }

    [Fact]
    public async Task MarkPaid_twice_reports_already_paid()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Pending);

        await WithServiceAsync(s => s.MarkPaidAsync(orderNo, NewTradeNo(), "Credit"));
        var second = await WithServiceAsync(s => s.MarkPaidAsync(orderNo, NewTradeNo(), "Credit"));

        Assert.Equal(OrderStateService.MarkPaidOutcome.AlreadyPaid, second);
    }

    [Fact]
    public async Task Cancelled_order_cannot_become_paid()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Cancelled);

        var outcome = await WithServiceAsync(s => s.MarkPaidAsync(orderNo, NewTradeNo(), "Credit"));

        Assert.Equal(OrderStateService.MarkPaidOutcome.NotPayable, outcome);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderNo)).Status);
    }

    [Fact]
    public async Task MarkPaid_unknown_order_reports_not_found()
    {
        var outcome = await WithServiceAsync(s => s.MarkPaidAsync("EHNOTEXIST000000000", NewTradeNo(), "Credit"));

        Assert.Equal(OrderStateService.MarkPaidOutcome.NotFound, outcome);
    }

    // ---------------- 逾期取消 ----------------

    [Fact]
    public async Task CancelExpired_cancels_only_expired_pending_orders_and_releases_seats()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (u1, _) = await CreateUserAsync(AppRoles.Attendee);
        var (u2, _) = await CreateUserAsync(AppRoles.Attendee);
        var (u3, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 3);
        var expired = await SeedOrderAsync(eventId, u1, organizerId, OrderStatus.Pending, TaiwanNow.AddMinutes(-1));
        var stillValid = await SeedOrderAsync(eventId, u2, organizerId, OrderStatus.Pending, TaiwanNow.AddMinutes(10));
        var paid = await SeedOrderAsync(eventId, u3, organizerId, OrderStatus.Paid);

        await WithServiceAsync(s => s.CancelExpiredAsync());

        var expiredOrder = await GetOrderAsync(expired);
        Assert.Equal(OrderStatus.Cancelled, expiredOrder.Status);
        Assert.NotNull(expiredOrder.CancelledAt);
        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(stillValid)).Status);
        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(paid)).Status);
        Assert.Equal(2, await GetReservedCountAsync(eventId));
    }

    [Fact]
    public async Task Payment_and_expiry_at_the_same_time_only_one_wins()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Pending, TaiwanNow.AddMinutes(-1));

        // 付款通知與逾期排程同時執行
        await Task.WhenAll(
            WithServiceAsync(s => s.MarkPaidAsync(orderNo, NewTradeNo(), "Credit")),
            WithServiceAsync(s => s.CancelExpiredAsync()));

        var order = await GetOrderAsync(orderNo);
        var reserved = await GetReservedCountAsync(eventId);
        if (order.Status == OrderStatus.Paid)
        {
            Assert.Equal(1, reserved); // 付款贏：名額保留
        }
        else
        {
            Assert.Equal(OrderStatus.Cancelled, order.Status);
            Assert.Equal(0, reserved); // 取消贏：名額釋放
        }
    }

    // ---------------- 報名者取消 ----------------

    private Task<HttpResponseMessage> PostCancelAsync(HttpClient client, string orderNo) =>
        client.PostFormAsync("/Orders", $"/Orders/{orderNo}/Cancel", new());

    [Fact]
    public async Task Attendee_can_cancel_own_pending_order()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, email) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Pending);
        var client = await LoginAsync(email);

        await PostCancelAsync(client, orderNo);

        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(0, await GetReservedCountAsync(eventId));
        Assert.Contains("已取消報名", await client.GetStringAsync($"/Orders/{orderNo}"));
    }

    [Fact]
    public async Task Attendee_cannot_cancel_paid_order()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, email) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Paid);
        var client = await LoginAsync(email);

        await PostCancelAsync(client, orderNo);

        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(1, await GetReservedCountAsync(eventId));
        Assert.Contains("已付款的報名無法自行取消", await client.GetStringAsync($"/Orders/{orderNo}"));
    }

    [Fact]
    public async Task Attendee_cannot_cancel_someone_elses_order()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (ownerId, _) = await CreateUserAsync(AppRoles.Attendee);
        var (_, strangerEmail) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, ownerId, organizerId, OrderStatus.Pending);
        var stranger = await LoginAsync(strangerEmail);

        await PostCancelAsync(stranger, orderNo);

        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(1, await GetReservedCountAsync(eventId));
    }

    // ---------------- 免費報名 ----------------

    [Fact]
    public async Task Attendee_can_cancel_free_registration_before_event_starts()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, email) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1, price: 0);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Paid, amount: 0);
        var client = await LoginAsync(email);
        Assert.Contains("取消報名", await client.GetStringAsync($"/Orders/{orderNo}"));

        await PostCancelAsync(client, orderNo);

        var order = await GetOrderAsync(orderNo);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAt);
        Assert.Equal(0, await GetReservedCountAsync(eventId));
        Assert.Contains("已取消報名", await client.GetStringAsync($"/Orders/{orderNo}"));
    }

    [Fact]
    public async Task Free_registration_cannot_be_cancelled_after_event_starts()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, email) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1, price: 0);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Paid, amount: 0);
        await factory.WithDbAsync(db => db.Events
            .Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.StartAt, TaiwanNow.AddMinutes(-10))));
        var client = await LoginAsync(email);

        await PostCancelAsync(client, orderNo);

        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(1, await GetReservedCountAsync(eventId));
        Assert.Contains("活動已開始，無法取消報名", await client.GetStringAsync($"/Orders/{orderNo}"));
    }

    [Fact]
    public async Task Free_registration_cannot_be_cancelled_when_event_is_cancelled()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1, price: 0);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Paid, amount: 0);
        await factory.WithDbAsync(db => db.Events
            .Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EventStatus.Cancelled)));

        var result = await WithServiceAsync(s => s.CancelByAttendeeAsync(orderNo, userId));

        Assert.False(result.Succeeded);
        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(orderNo)).Status);
    }

    [Fact]
    public async Task Free_registration_cannot_be_marked_refunded()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1, price: 0);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Paid, amount: 0);
        var admin = await LoginAsync(adminEmail);

        Assert.DoesNotContain("標記為已退款", await admin.GetStringAsync($"/Admin/Orders/{orderNo}"));

        await PostRefundAsync(admin, orderNo);

        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(1, await GetReservedCountAsync(eventId));
    }

    // ---------------- 我的報名列表 ----------------

    [Fact]
    public async Task My_registrations_hides_cancelled_and_expired_orders_but_keeps_event_cancellations()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, email) = await CreateUserAsync(AppRoles.Attendee);
        var openEvent = await SeedEventAsync(organizerId, reservedCount: 2);
        var cancelledEvent = await SeedEventAsync(organizerId);
        await factory.WithDbAsync(db => db.Events
            .Where(e => e.Id == cancelledEvent)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EventStatus.Cancelled)));

        var selfCancelled = await SeedOrderAsync(openEvent, userId, organizerId, OrderStatus.Cancelled);
        var expired = await SeedOrderAsync(openEvent, userId, organizerId, OrderStatus.Pending, TaiwanNow.AddMinutes(-1));
        var awaiting = await SeedOrderAsync(openEvent, userId, organizerId, OrderStatus.Pending, TaiwanNow.AddMinutes(10));
        var paid = await SeedOrderAsync(openEvent, userId, organizerId, OrderStatus.Paid);
        var cancelledByEvent = await SeedOrderAsync(cancelledEvent, userId, organizerId, OrderStatus.Cancelled);
        var client = await LoginAsync(email);

        var html = await client.GetStringAsync("/Orders");

        Assert.DoesNotContain(selfCancelled, html);
        Assert.DoesNotContain(expired, html);
        Assert.Contains(awaiting, html);
        Assert.Contains(paid, html);
        Assert.Contains(cancelledByEvent, html);

        // 不在列表上的訂單仍可從網址查看
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Orders/{selfCancelled}")).StatusCode);
    }

    // ---------------- 活動取消 ----------------

    [Fact]
    public async Task Cancelling_event_cancels_pending_orders_and_keeps_paid_ones()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var (u1, _) = await CreateUserAsync(AppRoles.Attendee);
        var (u2, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 2);
        var pending = await SeedOrderAsync(eventId, u1, organizerId, OrderStatus.Pending);
        var paid = await SeedOrderAsync(eventId, u2, organizerId, OrderStatus.Paid);
        var organizer = await LoginAsync(organizerEmail);

        await organizer.PostFormAsync($"/Organizer/Events/{eventId}/Edit", $"/Organizer/Events/{eventId}/Cancel", new());

        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(pending)).Status);
        Assert.Equal(OrderStatus.Paid, (await GetOrderAsync(paid)).Status);
        Assert.Equal(1, await GetReservedCountAsync(eventId));
        Assert.Contains("有 1 筆已付款的報名需要退款", await organizer.GetStringAsync($"/Organizer/Events/{eventId}/Edit"));
    }

    [Fact]
    public async Task Cancelling_free_event_cancels_free_registrations_without_refund()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var (u1, _) = await CreateUserAsync(AppRoles.Attendee);
        var (u2, attendeeEmail) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 2, price: 0);
        var free1 = await SeedOrderAsync(eventId, u1, organizerId, OrderStatus.Paid, amount: 0);
        var free2 = await SeedOrderAsync(eventId, u2, organizerId, OrderStatus.Paid, amount: 0);
        var organizer = await LoginAsync(organizerEmail);

        await organizer.PostFormAsync($"/Organizer/Events/{eventId}/Edit", $"/Organizer/Events/{eventId}/Cancel", new());

        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(free1)).Status);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(free2)).Status);
        Assert.Equal(0, await GetReservedCountAsync(eventId));

        var organizerPage = await organizer.GetStringAsync($"/Organizer/Events/{eventId}/Edit");
        Assert.Contains("活動已取消。", organizerPage);
        Assert.DoesNotContain("需要退款", organizerPage);

        var attendee = await LoginAsync(attendeeEmail);
        var orderPage = await attendee.GetStringAsync($"/Orders/{free2}");
        Assert.Contains("活動已取消，此筆報名一併取消", orderPage);
        Assert.DoesNotContain("辦理退款", orderPage);
    }

    // ---------------- 退款 ----------------

    private Task<HttpResponseMessage> PostRefundAsync(HttpClient client, string orderNo) =>
        client.PostFormAsync($"/Admin/Orders/{orderNo}", $"/Admin/Orders/{orderNo}/Refund", new());

    [Fact]
    public async Task Admin_can_mark_paid_order_as_refunded()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Paid, amount: 1500);
        var admin = await LoginAsync(adminEmail);

        await PostRefundAsync(admin, orderNo);

        var order = await GetOrderAsync(orderNo);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(1500, order.RefundAmount);
        Assert.NotNull(order.RefundedAt);
        Assert.Equal(0, await GetReservedCountAsync(eventId));
    }

    [Fact]
    public async Task Pending_order_cannot_be_refunded()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, _) = await CreateUserAsync(AppRoles.Attendee);
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var eventId = await SeedEventAsync(organizerId, reservedCount: 1);
        var orderNo = await SeedOrderAsync(eventId, userId, organizerId, OrderStatus.Pending);
        var admin = await LoginAsync(adminEmail);

        await PostRefundAsync(admin, orderNo);

        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(1, await GetReservedCountAsync(eventId));
    }

    [Theory]
    [InlineData(AppRoles.Attendee)]
    [InlineData(AppRoles.Organizer)]
    public async Task Only_admin_can_use_admin_order_pages(string role)
    {
        var (_, email) = await CreateUserAsync(role);
        var client = await LoginAsync(email);

        var response = await client.GetAsync("/Admin/Orders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }
}
