using System.Net;
using ClosedXML.Excel;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>後台：報名名單、金額彙總、Excel 匯出、訂單列表、撥款彙總、權限。</summary>
public class ReportTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private static DateTime TaiwanNow => DateTime.UtcNow.AddHours(8);

    // ---------------- 準備資料 ----------------

    private async Task<(string Id, string Email)> CreateUserAsync(string role, string? displayName = null)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = TestClientExtensions.NewEmail();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName ?? role };
        await userManager.CreateAsync(user, TestClientExtensions.DefaultPassword);
        await userManager.AddToRoleAsync(user, role);
        return (user.Id, email);
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email);
        return client;
    }

    /// <param name="daysFromNow">活動開始時間距今幾天；負數 = 已結束的活動</param>
    private Task<int> SeedEventAsync(
        string organizerId, int price = 1000, int daysFromNow = 30,
        EventStatus status = EventStatus.Published, string title = "報表測試活動") =>
        factory.WithDbAsync(async db =>
        {
            var start = TaiwanNow.Date.AddDays(daysFromNow).AddHours(14);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = title,
                StartAt = start,
                EndAt = start.AddHours(2),
                Location = "台中市西區",
                Price = price,
                Capacity = 50,
                ReservedCount = 0,
                Status = status,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Events.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

    /// <summary>直接建立訂單；手續費 5%（與正式設定相同的算法）。</summary>
    private Task<string> SeedOrderAsync(
        int eventId, string organizerId, string attendeeUserId, OrderStatus status,
        string attendeeName, int amount = 1000, string? note = null) =>
        factory.WithDbAsync(async db =>
        {
            var fee = (int)Math.Round(amount * 0.05m, MidpointRounding.AwayFromZero);
            var order = new Order
            {
                OrderNo = OrderNumberGenerator.Generate(TaiwanNow),
                EventId = eventId,
                UserId = attendeeUserId,
                OrganizerId = organizerId,
                AttendeeName = attendeeName,
                AttendeePhone = "0912345678",
                AttendeeEmail = $"{Guid.NewGuid():N}@attendee.test",
                Note = note,
                Amount = amount,
                PlatformFee = fee,
                OrganizerAmount = amount - fee,
                Status = status,
                ExpiresAt = status == OrderStatus.Pending ? TaiwanNow.AddMinutes(15) : null,
                PaidAt = status == OrderStatus.Paid ? TaiwanNow : null,
                CancelledAt = status == OrderStatus.Cancelled ? TaiwanNow : null,
                RefundedAt = status == OrderStatus.Refunded ? TaiwanNow : null,
                RefundAmount = status == OrderStatus.Refunded ? amount : null,
                CreatedAt = TaiwanNow,
                UpdatedAt = TaiwanNow,
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            return order.OrderNo;
        });

    /// <summary>
    /// 一場 NT$1,000 的活動：報名成功 2 筆（甲、乙）、待付款 1 筆（丙）、已取消 1 筆（丁）、已退款 1 筆（戊）。
    /// 已付款合計：實收 2,000、手續費 100、主辦方實拿 1,900。
    /// </summary>
    private async Task<(int EventId, string OrganizerId, string OrganizerEmail)> SeedEventWithOrdersAsync()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId);

        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "報名者甲");
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "報名者乙");
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Pending, "報名者丙");
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Cancelled, "報名者丁");
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Refunded, "報名者戊");

        return (eventId, organizerId, organizerEmail);
    }

    private static string RegistrationsUrl(int eventId) => $"/Organizer/Events/{eventId}/Registrations";

    // ---------------- 主辦方：報名名單 ----------------

    [Fact]
    public async Task Organizer_sees_active_registrations_and_paid_amounts()
    {
        var (eventId, _, organizerEmail) = await SeedEventWithOrdersAsync();
        var client = await LoginAsync(organizerEmail);

        var html = await client.GetStringAsync(RegistrationsUrl(eventId));

        // 預設「有效報名」：報名成功 + 待付款
        Assert.Contains("報名者甲", html);
        Assert.Contains("報名者乙", html);
        Assert.Contains("報名者丙", html);
        Assert.DoesNotContain("報名者丁", html);
        Assert.DoesNotContain("報名者戊", html);

        // 金額只算已付款的 2 筆
        Assert.Contains("2,000", html);
        Assert.Contains("1,900", html);
        Assert.Contains("另有 1 人待付款", html);
    }

    [Theory]
    [InlineData("Cancelled", "報名者丁", "報名者甲")]
    [InlineData("Refunded", "報名者戊", "報名者甲")]
    [InlineData("Pending", "報名者丙", "報名者乙")]
    public async Task Registrations_can_be_filtered_by_status(string filter, string expected, string notExpected)
    {
        var (eventId, _, organizerEmail) = await SeedEventWithOrdersAsync();
        var client = await LoginAsync(organizerEmail);

        var html = await client.GetStringAsync($"{RegistrationsUrl(eventId)}?filter={filter}");

        Assert.Contains(expected, html);
        Assert.DoesNotContain(notExpected, html);
    }

    [Fact]
    public async Task All_filter_lists_every_order()
    {
        var (eventId, _, organizerEmail) = await SeedEventWithOrdersAsync();
        var client = await LoginAsync(organizerEmail);

        var html = await client.GetStringAsync($"{RegistrationsUrl(eventId)}?filter=All");

        foreach (var name in new[] { "報名者甲", "報名者乙", "報名者丙", "報名者丁", "報名者戊" })
        {
            Assert.Contains(name, html);
        }
    }

    [Fact]
    public async Task Organizer_cannot_see_other_organizers_registrations()
    {
        var (eventId, _, _) = await SeedEventWithOrdersAsync();
        var (_, otherOrganizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var client = await LoginAsync(otherOrganizerEmail);

        var page = await client.GetAsync(RegistrationsUrl(eventId));
        var excel = await client.GetAsync($"{RegistrationsUrl(eventId)}/Export");

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, excel.StatusCode);
    }

    [Fact]
    public async Task Cancelled_event_with_paid_orders_shows_refund_notice()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, status: EventStatus.Cancelled);
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "等退款的人");
        var client = await LoginAsync(organizerEmail);

        var html = await client.GetStringAsync(RegistrationsUrl(eventId));

        Assert.Contains("由平台協助退款", html);
    }

    // ---------------- Excel 匯出 ----------------

    /// <summary>下載匯出的 Excel，回傳工作表、標題列的列號、欄名 → 欄號。</summary>
    private static async Task<(IXLWorksheet Sheet, int HeaderRow, Dictionary<string, int> Columns)> ReadExcelAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType!.MediaType);

        var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("報名名單");
        var headerRow = sheet.Column(1).CellsUsed().First(c => c.GetString() == "序號").Address.RowNumber;
        var columns = sheet.Row(headerRow).CellsUsed()
            .ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
        return (sheet, headerRow, columns);
    }

    [Fact]
    public async Task Excel_export_contains_registrations()
    {
        var (eventId, _, organizerEmail) = await SeedEventWithOrdersAsync();
        var client = await LoginAsync(organizerEmail);

        var response = await client.GetAsync($"{RegistrationsUrl(eventId)}/Export?filter=Paid");
        var (sheet, headerRow, col) = await ReadExcelAsync(response);

        Assert.StartsWith("報名名單_報表測試活動_", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("報表測試活動", sheet.Cell(1, 1).GetString());
        Assert.Contains("簽到", col.Keys);

        // 已付款 2 筆
        var rows = Enumerable.Range(headerRow + 1, 2).Select(sheet.Row).ToList();
        Assert.True(sheet.Row(headerRow + 3).IsEmpty());
        Assert.Equal(1, rows[0].Cell(col["序號"]).GetValue<int>());
        Assert.Equal(2, rows[1].Cell(col["序號"]).GetValue<int>());

        var first = rows.Single(r => r.Cell(col["姓名"]).GetString() == "報名者甲");
        Assert.Equal("報名成功", first.Cell(col["狀態"]).GetString());
        Assert.Equal(1000, first.Cell(col["金額"]).GetValue<int>());
        Assert.Equal(50, first.Cell(col["平台手續費"]).GetValue<int>());
        Assert.Equal(950, first.Cell(col["主辦方實拿"]).GetValue<int>());
        Assert.True(first.Cell(col["報名時間"]).Value.IsDateTime);

        // 手機以文字儲存，開頭的 0 不會不見
        var phone = first.Cell(col["手機"]);
        Assert.True(phone.Value.IsText);
        Assert.Equal("0912345678", phone.GetString());
    }

    [Fact]
    public async Task Excel_export_never_turns_input_into_formulas()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId);
        const string evilName = "=HYPERLINK(\"http://evil.test\")";
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, evilName, note: "素食, 不吃辣");
        var client = await LoginAsync(organizerEmail);

        var (sheet, headerRow, col) = await ReadExcelAsync(await client.GetAsync($"{RegistrationsUrl(eventId)}/Export"));

        var name = sheet.Cell(headerRow + 1, col["姓名"]);
        Assert.False(name.HasFormula);
        Assert.Equal(evilName, name.GetString());
        Assert.Equal("素食, 不吃辣", sheet.Cell(headerRow + 1, col["備註"]).GetString());
    }

    [Fact]
    public async Task Free_event_excel_has_no_money_columns()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, price: 0);
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "免費報名者", amount: 0);
        var client = await LoginAsync(organizerEmail);

        var (_, _, col) = await ReadExcelAsync(await client.GetAsync($"{RegistrationsUrl(eventId)}/Export"));

        Assert.DoesNotContain("金額", col.Keys);
        Assert.DoesNotContain("付款時間", col.Keys);
        Assert.Contains("姓名", col.Keys);
        Assert.Contains("簽到", col.Keys);
    }

    [Fact]
    public async Task Excel_export_works_with_no_registrations()
    {
        var (organizerId, organizerEmail) = await CreateUserAsync(AppRoles.Organizer);
        var eventId = await SeedEventAsync(organizerId);
        var client = await LoginAsync(organizerEmail);

        var (sheet, headerRow, _) = await ReadExcelAsync(await client.GetAsync($"{RegistrationsUrl(eventId)}/Export"));

        Assert.True(sheet.Row(headerRow + 1).IsEmpty());
    }

    // ---------------- 主辦方：我的活動收款摘要 ----------------

    [Fact]
    public async Task Organizer_dashboard_shows_payout_summary()
    {
        var (_, _, organizerEmail) = await SeedEventWithOrdersAsync();
        var client = await LoginAsync(organizerEmail);

        var html = await client.GetStringAsync("/Organizer");

        Assert.Contains("你的實拿", html);
        Assert.Contains("1,900", html);
    }

    // ---------------- 撥款彙總 ----------------

    [Fact]
    public async Task Payouts_are_split_by_ended_upcoming_and_cancelled_events()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer, "撥款測試主辦");
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);

        var ended = await SeedEventAsync(organizerId, daysFromNow: -3);
        var upcoming = await SeedEventAsync(organizerId, daysFromNow: 10);
        var cancelled = await SeedEventAsync(organizerId, daysFromNow: 20, status: EventStatus.Cancelled);
        var free = await SeedEventAsync(organizerId, price: 0, daysFromNow: -3);

        await SeedOrderAsync(ended, organizerId, attendeeId, OrderStatus.Paid, "A", amount: 1000);
        await SeedOrderAsync(ended, organizerId, attendeeId, OrderStatus.Paid, "B", amount: 1000);
        await SeedOrderAsync(ended, organizerId, attendeeId, OrderStatus.Refunded, "C", amount: 1000);
        await SeedOrderAsync(upcoming, organizerId, attendeeId, OrderStatus.Paid, "D", amount: 600);
        await SeedOrderAsync(upcoming, organizerId, attendeeId, OrderStatus.Pending, "E", amount: 600);
        await SeedOrderAsync(cancelled, organizerId, attendeeId, OrderStatus.Paid, "F", amount: 800);
        await SeedOrderAsync(free, organizerId, attendeeId, OrderStatus.Paid, "G", amount: 0);

        using var scope = factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var payout = Assert.Single(await reports.GetPayoutsAsync(organizerId));

        Assert.Equal("撥款測試主辦", payout.OrganizerName);

        Assert.Equal(2, payout.Ended.PaidCount);
        Assert.Equal(2000, payout.Ended.Amount);
        Assert.Equal(100, payout.Ended.PlatformFee);
        Assert.Equal(1900, payout.Ended.OrganizerAmount);

        Assert.Equal(1, payout.Upcoming.PaidCount);
        Assert.Equal(570, payout.Upcoming.OrganizerAmount);

        Assert.Equal(1, payout.ToRefund.PaidCount);
        Assert.Equal(800, payout.ToRefund.Amount);

        Assert.Equal(2600, payout.TotalAmount);
        Assert.Equal(2470, payout.TotalOrganizerAmount);
    }

    [Fact]
    public async Task Admin_payouts_page_lists_organizers()
    {
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer, "撥款頁面主辦");
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, daysFromNow: -2);
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "A");
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var admin = await LoginAsync(adminEmail);

        var html = await admin.GetStringAsync("/Admin/Payouts");

        Assert.Contains("撥款頁面主辦", html);
        Assert.Contains($"organizer={organizerId}", html);
    }

    // ---------------- 管理員：活動、訂單 ----------------

    [Fact]
    public async Task Admin_can_see_any_event_registrations_and_link_to_orders()
    {
        var (eventId, _, _) = await SeedEventWithOrdersAsync();
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var admin = await LoginAsync(adminEmail);

        var html = await admin.GetStringAsync($"/Admin/Events/{eventId}");

        Assert.Contains("報名者甲", html);
        Assert.Contains("應撥主辦方", html);
        Assert.Contains("href=\"/Admin/Orders/EH", html);

        var excel = await admin.GetAsync($"/Admin/Events/{eventId}/Export");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
    }

    [Fact]
    public async Task Admin_event_list_shows_paid_totals()
    {
        var title = $"總覽活動-{Guid.NewGuid():N}"[..20];
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId, price: 1500, title: title);
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "A", amount: 1500);
        await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, "B", amount: 1500);
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var admin = await LoginAsync(adminEmail);

        var html = await admin.GetStringAsync($"/Admin/Events?q={Uri.EscapeDataString(title)}");

        Assert.Contains(title, html);
        Assert.Contains("3,000", html); // 實收
        Assert.Contains("2,850", html); // 應撥主辦方
    }

    [Fact]
    public async Task Admin_order_list_filters_by_keyword_and_status()
    {
        var name = $"搜尋{Guid.NewGuid():N}"[..12];
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId);
        var paidNo = await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, name);
        var cancelledNo = await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Cancelled, name);
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var admin = await LoginAsync(adminEmail);

        var all = await admin.GetStringAsync($"/Admin/Orders?q={Uri.EscapeDataString(name)}");
        var paidOnly = await admin.GetStringAsync($"/Admin/Orders?q={Uri.EscapeDataString(name)}&status=Paid");
        var byOrderNo = await admin.GetStringAsync($"/Admin/Orders?q={paidNo}");

        Assert.Contains(paidNo, all);
        Assert.Contains(cancelledNo, all);
        Assert.Contains("共 2 筆", all);

        Assert.Contains(paidNo, paidOnly);
        Assert.DoesNotContain(cancelledNo, paidOnly);

        Assert.Contains(paidNo, byOrderNo);
        Assert.DoesNotContain(cancelledNo, byOrderNo);
    }

    [Fact]
    public async Task Admin_order_list_filters_by_date()
    {
        var name = $"日期{Guid.NewGuid():N}"[..12];
        var (organizerId, _) = await CreateUserAsync(AppRoles.Organizer);
        var (attendeeId, _) = await CreateUserAsync(AppRoles.Attendee);
        var eventId = await SeedEventAsync(organizerId);
        var orderNo = await SeedOrderAsync(eventId, organizerId, attendeeId, OrderStatus.Paid, name);
        var (_, adminEmail) = await CreateUserAsync(AppRoles.Admin);
        var admin = await LoginAsync(adminEmail);
        var q = Uri.EscapeDataString(name);
        var today = TaiwanNow.ToString("yyyy-MM-dd");
        var tomorrow = TaiwanNow.AddDays(1).ToString("yyyy-MM-dd");

        Assert.Contains(orderNo, await admin.GetStringAsync($"/Admin/Orders?q={q}&from={today}&to={today}"));
        Assert.DoesNotContain(orderNo, await admin.GetStringAsync($"/Admin/Orders?q={q}&from={tomorrow}"));
    }

    // ---------------- 權限 ----------------

    [Theory]
    [InlineData(AppRoles.Attendee, "/Admin/Events")]
    [InlineData(AppRoles.Organizer, "/Admin/Events")]
    [InlineData(AppRoles.Organizer, "/Admin/Payouts")]
    [InlineData(AppRoles.Attendee, "/Admin/Payouts")]
    public async Task Only_admin_can_use_admin_report_pages(string role, string url)
    {
        var (_, email) = await CreateUserAsync(role);
        var client = await LoginAsync(email);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Attendee_cannot_see_registrations()
    {
        var (eventId, _, _) = await SeedEventWithOrdersAsync();
        var (_, attendeeEmail) = await CreateUserAsync(AppRoles.Attendee);
        var client = await LoginAsync(attendeeEmail);

        var response = await client.GetAsync(RegistrationsUrl(eventId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }
}
