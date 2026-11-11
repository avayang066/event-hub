using System.Net;
using System.Text.RegularExpressions;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>綠界結帳（前往付款）與付款結果通知（ReturnURL）。</summary>
public partial class EcpayPaymentTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private const string NotifyUrl = "/Payments/Ecpay/Notify";

    private static DateTime TaiwanNow => DateTime.UtcNow.AddHours(8);

    /// <summary>綠界交易編號有唯一索引，每次產生不同的。</summary>
    private static string NewTradeNo() => Guid.NewGuid().ToString("N")[..20];

    // ---------------- 準備資料 ----------------

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

    /// <summary>建立活動（已佔 1 個名額）與一張訂單，回傳訂單編號與報名者 Email。</summary>
    private async Task<(string OrderNo, string AttendeeEmail)> SeedOrderAsync(
        OrderStatus status = OrderStatus.Pending, DateTime? expiresAt = null, int amount = 1200)
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (userId, email) = await CreateUserAsync(AppRoles.Attendee);

        var orderNo = await factory.WithDbAsync(async db =>
        {
            var start = TaiwanNow.AddDays(30);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = "綠界測試活動",
                StartAt = start,
                EndAt = start.AddHours(2),
                Location = "台北",
                Price = amount,
                Capacity = 10,
                ReservedCount = status is OrderStatus.Pending or OrderStatus.Paid ? 1 : 0,
                Status = EventStatus.Published,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            var order = new Order
            {
                OrderNo = OrderNumberGenerator.Generate(TaiwanNow),
                Event = e,
                UserId = userId,
                OrganizerId = organizerId,
                AttendeeName = "測試",
                AttendeePhone = "0912345678",
                AttendeeEmail = email,
                Amount = amount,
                PlatformFee = amount / 20,
                OrganizerAmount = amount - amount / 20,
                Status = status,
                ExpiresAt = status == OrderStatus.Pending ? expiresAt ?? TaiwanNow.AddMinutes(15) : null,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.OrderNo;
        });

        return (orderNo, email);
    }

    /// <summary>直接建立一筆付款嘗試（等同報名者按了「前往付款」），回傳 MerchantTradeNo。</summary>
    private Task<string> SeedAttemptAsync(string orderNo) =>
        factory.WithDbAsync(async db =>
        {
            var order = await db.Orders.SingleAsync(o => o.OrderNo == orderNo);
            var attempt = new PaymentAttempt
            {
                OrderId = order.Id,
                MerchantTradeNo = OrderNumberGenerator.GenerateMerchantTradeNo(TaiwanNow),
                Amount = order.Amount,
                CreatedAt = TaiwanNow,
            };
            db.PaymentAttempts.Add(attempt);
            await db.SaveChangesAsync();
            return attempt.MerchantTradeNo;
        });

    private Task<Order> GetOrderAsync(string orderNo) =>
        factory.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.OrderNo == orderNo));

    private Task<List<PaymentNotification>> GetNotificationsAsync(string merchantTradeNo) =>
        factory.WithDbAsync(db => db.PaymentNotifications.AsNoTracking()
            .Where(n => n.MerchantTradeNo == merchantTradeNo)
            .OrderBy(n => n.Id)
            .ToListAsync());

    private Task<int> CountAttemptsAsync(string orderNo) =>
        factory.WithDbAsync(db => db.PaymentAttempts.CountAsync(a => a.Order.OrderNo == orderNo));

    // ---------------- 綠界通知 ----------------

    /// <summary>綠界付款結果通知的欄位（依官方文件），並算好檢查碼。</summary>
    private static Dictionary<string, string> Notification(
        string merchantTradeNo, int amount, string tradeNo, string rtnCode = "1", string simulatePaid = "0")
    {
        var fields = new Dictionary<string, string>
        {
            ["MerchantID"] = TestWebAppFactory.EcpayMerchantId,
            ["MerchantTradeNo"] = merchantTradeNo,
            ["StoreID"] = "",
            ["RtnCode"] = rtnCode,
            ["RtnMsg"] = rtnCode == "1" ? "交易成功" : "交易失敗",
            ["TradeNo"] = tradeNo,
            ["TradeAmt"] = amount.ToString(),
            ["PaymentDate"] = "2026/11/11 10:00:00",
            ["PaymentType"] = "Credit_CreditCard",
            ["PaymentTypeChargeFee"] = "25",
            ["TradeDate"] = "2026/11/11 09:58:00",
            ["SimulatePaid"] = simulatePaid,
            ["CustomField1"] = "",
            ["CustomField2"] = "",
            ["CustomField3"] = "",
            ["CustomField4"] = "",
        };
        fields["CheckMacValue"] = EcpayCheckMac.Compute(
            fields, TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV);
        return fields;
    }

    private async Task<string> PostNotifyAsync(Dictionary<string, string> fields)
    {
        var response = await NewClient().PostAsync(NotifyUrl, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ================= 前往付款 =================

    [GeneratedRegex("<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")]
    private static partial Regex HiddenInputRegex();

    private static Dictionary<string, string> ParseHiddenFields(string html) =>
        HiddenInputRegex().Matches(html)
            .ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value));

    private static Task<HttpResponseMessage> PostPayAsync(HttpClient client, string orderNo) =>
        client.PostFormAsync("/Orders", $"/Orders/{orderNo}/Pay", new());

    [Fact]
    public async Task Pay_renders_auto_submit_form_with_valid_check_mac()
    {
        var (orderNo, email) = await SeedOrderAsync(amount: 1200);
        var client = await LoginAsync(email);

        var response = await PostPayAsync(client, orderNo);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"action=\"{TestWebAppFactory.EcpayCheckoutUrl}\"", html);
        Assert.DoesNotContain("__RequestVerificationToken\" type=\"hidden\" value=\"", html.Split("ecpay-checkout")[1]);

        var fields = ParseHiddenFields(html);
        Assert.True(EcpayCheckMac.Verify(fields, TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV));
        Assert.Equal(TestWebAppFactory.EcpayMerchantId, fields["MerchantID"]);
        Assert.Equal("1200", fields["TotalAmount"]);
        Assert.Equal("Credit", fields["ChoosePayment"]);
        Assert.Equal("https://eventhub.example.test/Payments/Ecpay/Notify", fields["ReturnURL"]);
        Assert.EndsWith($"/Orders/{orderNo}", fields["ClientBackURL"]);
        Assert.Equal(orderNo, fields["CustomField1"]);
        Assert.Contains("綠界測試活動", fields["ItemName"]);
        Assert.Matches("^EP[0-9A-Z]{18}$", fields["MerchantTradeNo"]);

        // 付款嘗試已記錄
        Assert.Equal(1, await CountAttemptsAsync(orderNo));
    }

    [Fact]
    public async Task Each_pay_click_uses_a_new_merchant_trade_no()
    {
        var (orderNo, email) = await SeedOrderAsync();
        var client = await LoginAsync(email);

        var first = ParseHiddenFields(await (await PostPayAsync(client, orderNo)).Content.ReadAsStringAsync());
        var second = ParseHiddenFields(await (await PostPayAsync(client, orderNo)).Content.ReadAsStringAsync());

        Assert.NotEqual(first["MerchantTradeNo"], second["MerchantTradeNo"]);
        Assert.Equal(2, await CountAttemptsAsync(orderNo));
    }

    [Fact]
    public async Task Cannot_pay_expired_order()
    {
        var (orderNo, email) = await SeedOrderAsync(expiresAt: TaiwanNow.AddMinutes(-1));
        var client = await LoginAsync(email);

        var response = await PostPayAsync(client, orderNo);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await CountAttemptsAsync(orderNo));
        Assert.Contains("已超過付款期限", await client.GetStringAsync($"/Orders/{orderNo}"));
    }

    [Fact]
    public async Task Cannot_pay_paid_order()
    {
        var (orderNo, email) = await SeedOrderAsync(OrderStatus.Paid);
        var client = await LoginAsync(email);

        await PostPayAsync(client, orderNo);

        Assert.Equal(0, await CountAttemptsAsync(orderNo));
    }

    [Fact]
    public async Task Cannot_pay_someone_elses_order()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var (_, strangerEmail) = await CreateUserAsync(AppRoles.Attendee);
        var stranger = await LoginAsync(strangerEmail);

        await PostPayAsync(stranger, orderNo);

        Assert.Equal(0, await CountAttemptsAsync(orderNo));
    }

    [Fact]
    public async Task Awaiting_payment_page_shows_pay_button()
    {
        var (orderNo, email) = await SeedOrderAsync();
        var client = await LoginAsync(email);

        var html = await client.GetStringAsync($"/Orders/{orderNo}");

        Assert.Contains($"action=\"/Orders/{orderNo}/Pay\"", html);
        Assert.Contains("前往付款", html);
    }

    // ================= 付款結果通知 =================

    [Fact]
    public async Task Successful_payment_notification_marks_order_paid()
    {
        var (orderNo, _) = await SeedOrderAsync(amount: 1200);
        var merchantTradeNo = await SeedAttemptAsync(orderNo);
        var tradeNo = NewTradeNo();

        var reply = await PostNotifyAsync(Notification(merchantTradeNo, 1200, tradeNo));

        Assert.Equal("1|OK", reply);
        var order = await GetOrderAsync(orderNo);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(tradeNo, order.EcpayTradeNo);
        Assert.Equal("Credit_CreditCard", order.PaymentType);
        Assert.NotNull(order.PaidAt);

        var log = Assert.Single(await GetNotificationsAsync(merchantTradeNo));
        Assert.Equal(PaymentProcessResult.Paid, log.ProcessResult);
        Assert.True(log.IsCheckMacValid);
        Assert.Equal(order.Id, log.OrderId);
        Assert.Contains("交易成功", log.RawPayload);
    }

    [Fact]
    public async Task Repeated_notification_is_processed_only_once()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var merchantTradeNo = await SeedAttemptAsync(orderNo);
        var fields = Notification(merchantTradeNo, 1200, NewTradeNo());

        await PostNotifyAsync(fields);
        var paidAt = (await GetOrderAsync(orderNo)).PaidAt;
        var reply = await PostNotifyAsync(fields);

        Assert.Equal("1|OK", reply); // 重複通知也要回 1|OK，綠界才不會一直重送
        Assert.Equal(paidAt, (await GetOrderAsync(orderNo)).PaidAt);
        Assert.Equal(
            new[] { PaymentProcessResult.Paid, PaymentProcessResult.Duplicate },
            (await GetNotificationsAsync(merchantTradeNo)).Select(n => n.ProcessResult).ToArray());
    }

    [Fact]
    public async Task Simultaneous_duplicate_notifications_mark_paid_once()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var merchantTradeNo = await SeedAttemptAsync(orderNo);
        var fields = Notification(merchantTradeNo, 1200, NewTradeNo());

        var replies = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostNotifyAsync(fields)));

        Assert.All(replies, r => Assert.Equal("1|OK", r));
        var results = (await GetNotificationsAsync(merchantTradeNo)).Select(n => n.ProcessResult).ToList();
        Assert.Equal(5, results.Count);
        Assert.Equal(1, results.Count(r => r == PaymentProcessResult.Paid));
        Assert.Equal(4, results.Count(r => r == PaymentProcessResult.Duplicate));
    }

    [Fact]
    public async Task Notification_with_wrong_check_mac_is_rejected()
    {
        var (orderNo, _) = await SeedOrderAsync(amount: 1200);
        var merchantTradeNo = await SeedAttemptAsync(orderNo);
        var fields = Notification(merchantTradeNo, 1200, NewTradeNo());
        fields["RtnCode"] = "1";
        fields["TradeAmt"] = "1"; // 竄改金額，檢查碼就對不上

        var reply = await PostNotifyAsync(fields);

        Assert.StartsWith("0|", reply);
        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
        var log = Assert.Single(await GetNotificationsAsync(merchantTradeNo));
        Assert.False(log.IsCheckMacValid);
        Assert.Equal(PaymentProcessResult.InvalidCheckMac, log.ProcessResult);
        Assert.Null(log.OrderId);
    }

    [Fact]
    public async Task Notification_for_another_merchant_is_rejected()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var merchantTradeNo = await SeedAttemptAsync(orderNo);
        var fields = Notification(merchantTradeNo, 1200, NewTradeNo());
        fields["MerchantID"] = "2000132";
        fields["CheckMacValue"] = EcpayCheckMac.Compute(fields, TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV);

        var reply = await PostNotifyAsync(fields);

        Assert.StartsWith("0|", reply);
        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
    }

    [Fact]
    public async Task Simulated_payment_does_not_mark_order_paid()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var merchantTradeNo = await SeedAttemptAsync(orderNo);

        var reply = await PostNotifyAsync(Notification(merchantTradeNo, 1200, NewTradeNo(), simulatePaid: "1"));

        Assert.Equal("1|OK", reply);
        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
        var log = Assert.Single(await GetNotificationsAsync(merchantTradeNo));
        Assert.True(log.IsSimulated);
        Assert.Equal(PaymentProcessResult.Simulated, log.ProcessResult);
    }

    [Fact]
    public async Task Failed_payment_does_not_mark_order_paid()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var merchantTradeNo = await SeedAttemptAsync(orderNo);

        await PostNotifyAsync(Notification(merchantTradeNo, 1200, NewTradeNo(), rtnCode: "10100058"));

        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(PaymentProcessResult.PaymentFailed,
            Assert.Single(await GetNotificationsAsync(merchantTradeNo)).ProcessResult);
    }

    [Fact]
    public async Task Amount_mismatch_does_not_mark_order_paid()
    {
        var (orderNo, _) = await SeedOrderAsync(amount: 1200);
        var merchantTradeNo = await SeedAttemptAsync(orderNo);

        await PostNotifyAsync(Notification(merchantTradeNo, 1, NewTradeNo()));

        Assert.Equal(OrderStatus.Pending, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(PaymentProcessResult.AmountMismatch,
            Assert.Single(await GetNotificationsAsync(merchantTradeNo)).ProcessResult);
    }

    [Fact]
    public async Task Unknown_merchant_trade_no_is_logged_and_acknowledged()
    {
        var merchantTradeNo = OrderNumberGenerator.GenerateMerchantTradeNo(TaiwanNow);

        var reply = await PostNotifyAsync(Notification(merchantTradeNo, 1200, NewTradeNo()));

        Assert.Equal("1|OK", reply);
        Assert.Equal(PaymentProcessResult.OrderNotFound,
            Assert.Single(await GetNotificationsAsync(merchantTradeNo)).ProcessResult);
    }

    [Fact]
    public async Task Payment_after_cancellation_is_flagged_for_refund()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var merchantTradeNo = await SeedAttemptAsync(orderNo);
        // 報名者在綠界付款頁停留太久，訂單先被逾期取消
        await factory.WithDbAsync(db => db.Orders
            .Where(o => o.OrderNo == orderNo)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled)));
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);

        var reply = await PostNotifyAsync(Notification(merchantTradeNo, 1200, NewTradeNo()));

        Assert.Equal("1|OK", reply);
        Assert.Equal(OrderStatus.Cancelled, (await GetOrderAsync(orderNo)).Status);
        Assert.Equal(PaymentProcessResult.PaidAfterCancelled,
            Assert.Single(await GetNotificationsAsync(merchantTradeNo)).ProcessResult);

        var admin = await LoginAsync(adminEmail);
        var html = await admin.GetStringAsync($"/Admin/Orders/{orderNo}");
        Assert.Contains("此訂單有需要處理的付款紀錄", html);
        Assert.Contains("訂單取消後才付款（需退款）", html);
    }

    [Fact]
    public async Task Second_payment_for_paid_order_is_flagged_as_duplicate_payment()
    {
        var (orderNo, _) = await SeedOrderAsync();
        var firstAttempt = await SeedAttemptAsync(orderNo);
        var secondAttempt = await SeedAttemptAsync(orderNo);
        var firstTradeNo = NewTradeNo();

        await PostNotifyAsync(Notification(firstAttempt, 1200, firstTradeNo));
        await PostNotifyAsync(Notification(secondAttempt, 1200, NewTradeNo()));

        Assert.Equal(firstTradeNo, (await GetOrderAsync(orderNo)).EcpayTradeNo);
        Assert.Equal(PaymentProcessResult.DuplicatePayment,
            Assert.Single(await GetNotificationsAsync(secondAttempt)).ProcessResult);
    }

    [Fact]
    public async Task Notify_endpoint_does_not_require_login_or_antiforgery()
    {
        var response = await NewClient().PostAsync(NotifyUrl, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("0|", await response.Content.ReadAsStringAsync());
    }
}
