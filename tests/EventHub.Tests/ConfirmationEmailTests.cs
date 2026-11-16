using System.Net;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>報名成功信：寄送時機、內容、不重複寄、失敗重寄。</summary>
public class ConfirmationEmailTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private static DateTime TaiwanNow => DateTime.UtcNow.AddHours(8);

    private static string NewAttendeeEmail() => $"attendee-{Guid.NewGuid():N}@test.local";

    // ---------------- 準備資料 ----------------

    private async Task<string> CreateUserAsync(string role, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        email ??= TestClientExtensions.NewEmail();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = role };
        await userManager.CreateAsync(user, TestClientExtensions.DefaultPassword);
        await userManager.AddToRoleAsync(user, role);
        return user.Id;
    }

    private Task<int> SeedEventAsync(string organizerId, int price = 1200, string title = "寄信測試活動") =>
        factory.WithDbAsync(async db =>
        {
            var start = new DateTime(TaiwanNow.Year + 1, 3, 15, 14, 0, 0);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = title,
                StartAt = start,
                EndAt = start.AddHours(2),
                Location = "台北市信義區松仁路 100 號",
                Price = price,
                Capacity = 10,
                ReservedCount = 1,
                Status = EventStatus.Published,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

    /// <summary>直接建立訂單（不經過報名流程，不會自動寄信）。</summary>
    private async Task<string> SeedOrderAsync(
        string attendeeEmail, OrderStatus status = OrderStatus.Paid, int price = 1200, string title = "寄信測試活動")
    {
        var organizerId = await CreateUserAsync(AppRoles.Organizer);
        var userId = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, price, title);

        return await factory.WithDbAsync(async db =>
        {
            var order = new Order
            {
                OrderNo = OrderNumberGenerator.Generate(TaiwanNow),
                EventId = eventId,
                UserId = userId,
                OrganizerId = organizerId,
                AttendeeName = "王小明",
                AttendeePhone = "0912345678",
                AttendeeEmail = attendeeEmail,
                Amount = price,
                PlatformFee = price / 20,
                OrganizerAmount = price - price / 20,
                Status = status,
                ExpiresAt = status == OrderStatus.Pending ? TaiwanNow.AddMinutes(15) : null,
                PaidAt = status == OrderStatus.Paid ? TaiwanNow : null,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.OrderNo;
        });
    }

    private Task<Order> GetOrderAsync(string orderNo) =>
        factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.OrderNo == orderNo));

    private async Task<T> WithServiceAsync<T>(Func<ConfirmationEmailService, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ConfirmationEmailService>());
    }

    // ---------------- 內容 ----------------

    [Fact]
    public async Task Email_contains_event_and_order_details()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email, price: 1200);

        var outcome = await WithServiceAsync(s => s.SendAsync(orderNo));

        Assert.Equal(ConfirmationEmailService.SendOutcome.Sent, outcome);
        var message = Assert.Single(factory.Emails.SentTo(email));
        Assert.Equal("王小明", message.ToName);
        Assert.Equal("【EventHub】報名成功：寄信測試活動", message.Subject);
        foreach (var body in new[] { message.HtmlBody, message.TextBody })
        {
            Assert.Contains("王小明", body);
            Assert.Contains("寄信測試活動", body);
            Assert.Contains("台北市信義區松仁路 100 號", body);
            Assert.Contains("14:00 – 16:00", body);
            Assert.Contains("NT$ 1,200", body);
            Assert.Contains(orderNo, body);
            Assert.Contains($"https://eventhub.example.test/Orders/{orderNo}", body);
        }

        Assert.NotNull((await GetOrderAsync(orderNo)).ConfirmationEmailSentAt);
    }

    [Fact]
    public async Task Free_event_email_shows_free()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email, price: 0);

        await WithServiceAsync(s => s.SendAsync(orderNo));

        var message = Assert.Single(factory.Emails.SentTo(email));
        Assert.Contains("免費", message.TextBody);
        Assert.DoesNotContain("NT$", message.TextBody);
    }

    [Fact]
    public async Task Html_in_event_title_is_encoded()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email, title: "<script>alert(1)</script>講座");

        await WithServiceAsync(s => s.SendAsync(orderNo));

        var message = Assert.Single(factory.Emails.SentTo(email));
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
    }

    // ---------------- 不重複寄、不該寄的不寄 ----------------

    [Fact]
    public async Task Email_is_sent_only_once()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WithServiceAsync(s => s.SendAsync(orderNo))));
        var again = await WithServiceAsync(s => s.SendAsync(orderNo));

        Assert.Equal(1, outcomes.Count(o => o == ConfirmationEmailService.SendOutcome.Sent));
        Assert.Equal(ConfirmationEmailService.SendOutcome.AlreadySent, again);
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task Unpaid_or_cancelled_orders_get_no_email(OrderStatus status)
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email, status);

        var outcome = await WithServiceAsync(s => s.SendAsync(orderNo));

        Assert.Equal(ConfirmationEmailService.SendOutcome.NotEligible, outcome);
        Assert.Empty(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task Cancelled_event_gets_no_email()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email);
        var eventId = (await GetOrderAsync(orderNo)).EventId;
        await factory.WithDbAsync(db => db.Events
            .Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, EventStatus.Cancelled)));

        var outcome = await WithServiceAsync(s => s.SendAsync(orderNo));

        Assert.Equal(ConfirmationEmailService.SendOutcome.NotEligible, outcome);
        Assert.Empty(factory.Emails.SentTo(email));
    }

    // ---------------- 失敗重寄 ----------------

    [Fact]
    public async Task Failed_email_is_retried_later()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email);
        factory.Emails.FailingRecipients[email] = true;

        var first = await WithServiceAsync(s => s.SendAsync(orderNo));

        Assert.Equal(ConfirmationEmailService.SendOutcome.Failed, first);
        Assert.Null((await GetOrderAsync(orderNo)).ConfirmationEmailSentAt);

        // SMTP 恢復後，補寄排程把它寄出
        factory.Emails.FailingRecipients.TryRemove(email, out _);
        await WithServiceAsync(s => s.SendPendingAsync());

        Assert.Single(factory.Emails.SentTo(email));
        Assert.NotNull((await GetOrderAsync(orderNo)).ConfirmationEmailSentAt);
    }

    [Fact]
    public async Task Retry_skips_orders_paid_long_ago()
    {
        var email = NewAttendeeEmail();
        var orderNo = await SeedOrderAsync(email);
        await factory.WithDbAsync(db => db.Orders
            .Where(o => o.OrderNo == orderNo)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaidAt, TaiwanNow.AddDays(-3))));

        await WithServiceAsync(s => s.SendPendingAsync());

        Assert.Empty(factory.Emails.SentTo(email));
    }

    // ---------------- 寄送時機（完整流程） ----------------

    [Fact]
    public async Task Free_registration_sends_email()
    {
        var organizerId = await CreateUserAsync(AppRoles.Organizer);
        var eventId = await SeedEventAsync(organizerId, price: 0);
        var loginEmail = TestClientExtensions.NewEmail();
        await CreateUserAsync(AppRoles.Attendee, loginEmail);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(loginEmail);
        var attendeeEmail = NewAttendeeEmail();

        var response = await client.PostFormAsync($"/Events/{eventId}", $"/Events/{eventId}/Register", new()
        {
            ["AttendeeName"] = "陳小華",
            ["AttendeePhone"] = "0912345678",
            ["AttendeeEmail"] = attendeeEmail,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var message = await factory.Emails.WaitForAsync(attendeeEmail);
        Assert.Equal("陳小華", message.ToName);
        Assert.Contains("免費", message.TextBody);
    }

    [Fact]
    public async Task Paid_event_registration_does_not_send_email_before_payment()
    {
        var organizerId = await CreateUserAsync(AppRoles.Organizer);
        var eventId = await SeedEventAsync(organizerId, price: 800);
        var loginEmail = TestClientExtensions.NewEmail();
        await CreateUserAsync(AppRoles.Attendee, loginEmail);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(loginEmail);
        var attendeeEmail = NewAttendeeEmail();

        await client.PostFormAsync($"/Events/{eventId}", $"/Events/{eventId}/Register", new()
        {
            ["AttendeeName"] = "陳小華",
            ["AttendeePhone"] = "0912345678",
            ["AttendeeEmail"] = attendeeEmail,
        });

        await Assert.ThrowsAsync<TimeoutException>(() => factory.Emails.WaitForAsync(attendeeEmail, TimeSpan.FromSeconds(1)));
    }
}
