using System.Net;
using System.Text.RegularExpressions;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

public class OrganizerEventsTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    private const string CreateUrl = "/Organizer/Events/Create";
    private static string EditUrl(int id) => $"/Organizer/Events/{id}/Edit";

    // ---------------- 準備資料 ----------------

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>註冊一個已登入的帳號，回傳 client 與使用者 Id。</summary>
    private async Task<(HttpClient Client, string UserId)> SignUpAsync(string accountType = "Organizer")
    {
        var client = NewClient();
        var email = TestClientExtensions.NewEmail();
        await client.RegisterAsync(email, accountType);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        return (client, user!.Id);
    }

    /// <summary>直接在資料庫建立活動（可指定狀態、已報名人數、時間）。</summary>
    private Task<int> SeedEventAsync(
        string organizerId,
        EventStatus status = EventStatus.Draft,
        int reservedCount = 0,
        int price = 500,
        int capacity = 50,
        DateTime? startAt = null) =>
        factory.WithDbAsync(async db =>
        {
            var start = startAt ?? DateTime.Now.AddDays(30);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = "測試活動",
                StartAt = start,
                EndAt = start.AddHours(2),
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

    /// <summary>一組合法的活動表單，可用 overrides 覆寫個別欄位。</summary>
    private static Dictionary<string, string> EventForm(Dictionary<string, string>? overrides = null)
    {
        var start = DateTime.Now.AddDays(30).Date.AddHours(14);
        var form = new Dictionary<string, string>
        {
            ["Title"] = "週末手作課",
            ["Description"] = "自備圍裙",
            ["StartAt"] = start.ToString("yyyy-MM-ddTHH:mm"),
            ["EndAt"] = start.AddHours(3).ToString("yyyy-MM-ddTHH:mm"),
            ["RegistrationDeadline"] = "",
            ["Location"] = "台北市大安區",
            ["Price"] = "800",
            ["Capacity"] = "20",
        };

        foreach (var (key, value) in overrides ?? new())
        {
            form[key] = value;
        }

        return form;
    }

    /// <summary>
    /// 確認某欄位「實際顯示」了錯誤訊息。
    /// 不能只比對文字：Required / Range 的訊息本來就寫在 HTML 的 data-val-* 屬性裡（給瀏覽器驗證用）。
    /// </summary>
    private static async Task AssertFieldErrorAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches(
            $"field-validation-error\"[^>]*data-valmsg-for=\"{field}\"[^>]*>{Regex.Escape(message)}",
            html);
    }

    private static int EventIdFromRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var segments = response.Headers.Location!.OriginalString.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(segments[^2]); // /Organizer/Events/{id}/Edit
    }

    // ---------------- 新增 ----------------

    [Fact]
    public async Task Organizer_can_create_event_as_draft()
    {
        var (client, organizerId) = await SignUpAsync();

        var response = await client.PostMultipartAsync(CreateUrl, CreateUrl, EventForm());

        var id = EventIdFromRedirect(response);
        var e = await GetEventAsync(id);
        Assert.Equal(organizerId, e.OrganizerId);
        Assert.Equal(EventStatus.Draft, e.Status);
        Assert.Equal("週末手作課", e.Title);
        Assert.Equal(800, e.Price);
        Assert.Equal(20, e.Capacity);
        Assert.Equal(0, e.ReservedCount);
        Assert.Null(e.CoverImagePath);
    }

    [Fact]
    public async Task Create_with_png_saves_cover_image()
    {
        var (client, _) = await SignUpAsync();

        var response = await client.PostMultipartAsync(CreateUrl, CreateUrl, EventForm(), TestFile.Png());

        var e = await GetEventAsync(EventIdFromRedirect(response));
        Assert.NotNull(e.CoverImagePath);
        Assert.StartsWith("/uploads/events/", e.CoverImagePath);
        Assert.EndsWith(".png", e.CoverImagePath);
        Assert.True(File.Exists(Path.Combine(factory.UploadRoot, "events", Path.GetFileName(e.CoverImagePath))));
    }

    [Fact]
    public async Task Create_rejects_file_that_is_not_really_an_image()
    {
        var (client, _) = await SignUpAsync();

        var response = await client.PostMultipartAsync(CreateUrl, CreateUrl, EventForm(), TestFile.FakeJpg());

        await AssertFieldErrorAsync(response, "CoverImage", "只接受 JPG、PNG、WebP 格式的圖片");
    }

    [Fact]
    public async Task Create_rejects_image_over_5mb()
    {
        var (client, _) = await SignUpAsync();

        var response = await client.PostMultipartAsync(
            CreateUrl, CreateUrl, EventForm(), TestFile.Png(5 * 1024 * 1024 + 1));

        await AssertFieldErrorAsync(response, "CoverImage", "圖片不能超過 5MB");
    }

    [Theory]
    [InlineData("EndAt", "BEFORE_START", "結束時間必須晚於開始時間")]
    [InlineData("RegistrationDeadline", "AFTER_START", "報名截止時間不能晚於開始時間")]
    [InlineData("StartAt", "PAST", "開始時間必須晚於現在")]
    [InlineData("Price", "-1", "報名費需介於 0 到 999999 元")]
    [InlineData("Capacity", "0", "名額需介於 1 到 100000 人")]
    [InlineData("Title", "", "請輸入活動標題")]
    public async Task Create_validates_fields(string field, string value, string expectedError)
    {
        var (client, _) = await SignUpAsync();
        var form = EventForm();
        var start = DateTime.Parse(form["StartAt"]);

        form[field] = value switch
        {
            "BEFORE_START" => start.AddHours(-1).ToString("yyyy-MM-ddTHH:mm"),
            "AFTER_START" => start.AddHours(1).ToString("yyyy-MM-ddTHH:mm"),
            "PAST" => DateTime.Now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm"),
            _ => value,
        };
        if (value == "PAST")
        {
            form["EndAt"] = DateTime.Now.AddDays(-1).AddHours(2).ToString("yyyy-MM-ddTHH:mm");
        }

        var response = await client.PostMultipartAsync(CreateUrl, CreateUrl, form);

        await AssertFieldErrorAsync(response, field, expectedError);
    }

    // ---------------- 權限 ----------------

    [Fact]
    public async Task Attendee_cannot_open_create_page()
    {
        var (client, _) = await SignUpAsync("Attendee");

        var response = await client.GetAsync(CreateUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Organizer_cannot_view_or_edit_other_organizers_event()
    {
        var (_, ownerId) = await SignUpAsync();
        var eventId = await SeedEventAsync(ownerId);
        var (otherClient, _) = await SignUpAsync();

        var get = await otherClient.GetAsync(EditUrl(eventId));
        // 防偽權杖從自己的新增頁取得，再嘗試改別人的活動
        var post = await otherClient.PostMultipartAsync(CreateUrl, EditUrl(eventId), EventForm(new() { ["Title"] = "被竄改" }));
        var publish = await otherClient.PostFormAsync(CreateUrl, $"/Organizer/Events/{eventId}/Publish", new());

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, publish.StatusCode);
        var e = await GetEventAsync(eventId);
        Assert.Equal("測試活動", e.Title);
        Assert.Equal(EventStatus.Draft, e.Status);
    }

    [Fact]
    public async Task Index_lists_only_own_events()
    {
        var (client, organizerId) = await SignUpAsync();
        var (_, otherId) = await SignUpAsync();
        await factory.WithDbAsync(async db =>
        {
            var mine = await SeedEventAsync(organizerId);
            var others = await SeedEventAsync(otherId);
            await db.Events.Where(e => e.Id == mine).ExecuteUpdateAsync(s => s.SetProperty(e => e.Title, "我的活動A"));
            await db.Events.Where(e => e.Id == others).ExecuteUpdateAsync(s => s.SetProperty(e => e.Title, "別人的活動B"));
        });

        var html = await client.GetStringAsync("/Organizer");

        Assert.Contains("我的活動A", html);
        Assert.DoesNotContain("別人的活動B", html);
    }

    // ---------------- 編輯 ----------------

    [Fact]
    public async Task Organizer_can_edit_own_event()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId);

        var response = await client.PostMultipartAsync(EditUrl(id), EditUrl(id),
            EventForm(new() { ["Title"] = "新標題", ["Price"] = "1200", ["Capacity"] = "30" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var e = await GetEventAsync(id);
        Assert.Equal("新標題", e.Title);
        Assert.Equal(1200, e.Price);
        Assert.Equal(30, e.Capacity);
    }

    [Fact]
    public async Task Edit_rejects_capacity_below_reserved_count()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Published, reservedCount: 10, price: 800);

        var response = await client.PostMultipartAsync(EditUrl(id), EditUrl(id),
            EventForm(new() { ["Capacity"] = "5" }));

        await AssertFieldErrorAsync(response, "Capacity", "已有 10 人報名，名額不能少於 10");
        Assert.Equal(50, (await GetEventAsync(id)).Capacity);
    }

    [Fact]
    public async Task Edit_rejects_price_change_after_registrations()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Published, reservedCount: 1, price: 500);

        var response = await client.PostMultipartAsync(EditUrl(id), EditUrl(id),
            EventForm(new() { ["Price"] = "900" }));

        await AssertFieldErrorAsync(response, "Price", "已有人報名，不能修改報名費");
        Assert.Equal(500, (await GetEventAsync(id)).Price);
    }

    [Fact]
    public async Task Edit_allows_other_changes_after_registrations_when_price_unchanged()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Published, reservedCount: 3, price: 800);

        var response = await client.PostMultipartAsync(EditUrl(id), EditUrl(id),
            EventForm(new() { ["Location"] = "新竹市", ["Capacity"] = "3" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var e = await GetEventAsync(id);
        Assert.Equal("新竹市", e.Location);
        Assert.Equal(3, e.Capacity);
    }

    [Fact]
    public async Task Edit_replaces_cover_image_and_deletes_old_file()
    {
        var (client, _) = await SignUpAsync();
        var created = await client.PostMultipartAsync(CreateUrl, CreateUrl, EventForm(), TestFile.Png());
        var id = EventIdFromRedirect(created);
        var oldPath = (await GetEventAsync(id)).CoverImagePath!;

        await client.PostMultipartAsync(EditUrl(id), EditUrl(id), EventForm(), TestFile.Png());

        var newPath = (await GetEventAsync(id)).CoverImagePath!;
        Assert.NotEqual(oldPath, newPath);
        Assert.False(File.Exists(Path.Combine(factory.UploadRoot, "events", Path.GetFileName(oldPath))));
        Assert.True(File.Exists(Path.Combine(factory.UploadRoot, "events", Path.GetFileName(newPath))));
    }

    [Fact]
    public async Task Edit_cancelled_event_is_rejected()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Cancelled);

        var response = await client.PostMultipartAsync(EditUrl(id), EditUrl(id),
            EventForm(new() { ["Title"] = "取消後還想改" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("已取消的活動不能修改", await response.Content.ReadAsStringAsync());
        Assert.Equal("測試活動", (await GetEventAsync(id)).Title);
    }

    // ---------------- 狀態變更 ----------------

    private Task<HttpResponseMessage> PostActionAsync(HttpClient client, int id, string action) =>
        client.PostFormAsync(EditUrl(id), $"/Organizer/Events/{id}/{action}", new());

    [Fact]
    public async Task Publish_draft_succeeds()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId);

        await PostActionAsync(client, id, "Publish");

        Assert.Equal(EventStatus.Published, (await GetEventAsync(id)).Status);
    }

    [Fact]
    public async Task Publish_rejects_event_that_already_started()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, startAt: DateTime.Now.AddDays(-1));

        await PostActionAsync(client, id, "Publish");

        Assert.Equal(EventStatus.Draft, (await GetEventAsync(id)).Status);
        Assert.Contains("活動開始時間已過", await client.GetStringAsync(EditUrl(id)));
    }

    [Fact]
    public async Task Unpublish_without_registrations_returns_to_draft()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Published);

        await PostActionAsync(client, id, "Unpublish");

        Assert.Equal(EventStatus.Draft, (await GetEventAsync(id)).Status);
    }

    [Fact]
    public async Task Unpublish_is_rejected_when_event_has_registrations()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Published, reservedCount: 1);

        await PostActionAsync(client, id, "Unpublish");

        Assert.Equal(EventStatus.Published, (await GetEventAsync(id)).Status);
        Assert.Contains("已有人報名，不能下架", await client.GetStringAsync(EditUrl(id)));
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.Published)]
    public async Task Cancel_event(EventStatus from)
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, from, reservedCount: from == EventStatus.Published ? 2 : 0);

        await PostActionAsync(client, id, "Cancel");

        Assert.Equal(EventStatus.Cancelled, (await GetEventAsync(id)).Status);
    }

    [Fact]
    public async Task Cancelled_event_cannot_be_published_again()
    {
        var (client, organizerId) = await SignUpAsync();
        var id = await SeedEventAsync(organizerId, EventStatus.Cancelled);

        await PostActionAsync(client, id, "Publish");

        Assert.Equal(EventStatus.Cancelled, (await GetEventAsync(id)).Status);
    }
}
