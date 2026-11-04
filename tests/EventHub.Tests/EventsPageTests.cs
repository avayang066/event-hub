using System.Net;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>前台：首頁活動列表、活動詳情頁。</summary>
public class EventsPageTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    // ---------------- 準備資料 ----------------

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>直接建立一個主辦方帳號（不經過註冊頁）。</summary>
    private async Task<string> CreateOrganizerAsync(string displayName = "測試主辦")
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = TestClientExtensions.NewEmail();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName };
        await userManager.CreateAsync(user, TestClientExtensions.DefaultPassword);
        await userManager.AddToRoleAsync(user, AppRoles.Organizer);
        return user.Id;
    }

    private Task<int> SeedEventAsync(
        string organizerId,
        string title,
        EventStatus status = EventStatus.Published,
        DateTime? startAt = null,
        int capacity = 10,
        int reservedCount = 0,
        int price = 1200,
        string? description = null) =>
        factory.WithDbAsync(async db =>
        {
            var start = startAt ?? DateTime.Now.AddDays(30);
            var e = new Event
            {
                OrganizerId = organizerId,
                Title = title,
                Description = description,
                StartAt = start,
                EndAt = start.AddHours(2),
                Location = "台北市信義區",
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

    /// <summary>列表測試前清空活動，避免其他測試建立的活動影響排序與分頁。</summary>
    private Task ClearEventsAsync() => factory.WithDbAsync(db => db.Events.ExecuteDeleteAsync());

    // ---------------- 首頁活動列表 ----------------

    [Fact]
    public async Task Home_lists_only_published_events_that_have_not_ended()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "公開的活動");
        await SeedEventAsync(organizer, "草稿活動", EventStatus.Draft);
        await SeedEventAsync(organizer, "取消的活動", EventStatus.Cancelled);
        await SeedEventAsync(organizer, "已結束的活動", startAt: DateTime.Now.AddDays(-2));

        var html = await NewClient().GetStringAsync("/");

        Assert.Contains("公開的活動", html);
        Assert.DoesNotContain("草稿活動", html);
        Assert.DoesNotContain("取消的活動", html);
        Assert.DoesNotContain("已結束的活動", html);
    }

    [Fact]
    public async Task Home_orders_events_by_start_time()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "比較晚的活動", startAt: DateTime.Now.AddDays(10));
        await SeedEventAsync(organizer, "比較早的活動", startAt: DateTime.Now.AddDays(3));

        var html = await NewClient().GetStringAsync("/");

        Assert.True(html.IndexOf("比較早的活動", StringComparison.Ordinal) < html.IndexOf("比較晚的活動", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Home_shows_full_events_with_status()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "額滿的活動", capacity: 5, reservedCount: 5);

        var html = await NewClient().GetStringAsync("/");

        Assert.Contains("額滿的活動", html);
        Assert.Contains("已額滿", html);
    }

    [Fact]
    public async Task Home_paginates_20_events_per_page()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        for (var i = 1; i <= 21; i++)
        {
            await SeedEventAsync(organizer, $"分頁活動-{i:D2}", startAt: DateTime.Now.AddDays(i));
        }

        var client = NewClient();
        var page1 = await client.GetStringAsync("/");
        var page2 = await client.GetStringAsync("/?page=2");

        Assert.Contains("分頁活動-01", page1);
        Assert.Contains("分頁活動-20", page1);
        Assert.DoesNotContain("分頁活動-21", page1);
        Assert.Contains("下一頁", page1);

        Assert.Contains("分頁活動-21", page2);
        Assert.DoesNotContain("分頁活動-20", page2);
        Assert.Contains("上一頁", page2);
        Assert.DoesNotContain("下一頁", page2);
    }

    // ---------------- 搜尋 ----------------

    private async Task<string> SearchAsync(string keyword, int page = 1) =>
        await NewClient().GetStringAsync($"/?q={Uri.EscapeDataString(keyword)}&page={page}");

    [Fact]
    public async Task Search_matches_part_of_title()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "親子陶藝體驗");
        await SeedEventAsync(organizer, "瑜珈晨課");

        var html = await SearchAsync("陶藝");

        Assert.Contains("親子陶藝體驗", html);
        Assert.DoesNotContain("瑜珈晨課", html);
        Assert.Contains("「陶藝」的搜尋結果", html);
    }

    [Fact]
    public async Task Search_ignores_case_for_english()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "Python 入門工作坊");

        var html = await SearchAsync("python");

        Assert.Contains("Python 入門工作坊", html);
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_as_plain_text()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "100% 好玩市集");
        await SeedEventAsync(organizer, "普通活動");

        var html = await SearchAsync("%");
        var underscore = await SearchAsync("_");

        Assert.Contains("100% 好玩市集", html);
        Assert.DoesNotContain("普通活動", html);
        Assert.DoesNotContain("普通活動", underscore);
    }

    [Fact]
    public async Task Search_only_returns_published_events()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "咖啡講座上架");
        await SeedEventAsync(organizer, "咖啡講座草稿", EventStatus.Draft);

        var html = await SearchAsync("咖啡講座");

        Assert.Contains("咖啡講座上架", html);
        Assert.DoesNotContain("咖啡講座草稿", html);
    }

    [Fact]
    public async Task Search_without_results_shows_message()
    {
        await ClearEventsAsync();

        var html = await SearchAsync("不存在的活動名稱");

        Assert.Contains("找不到符合「不存在的活動名稱」的活動", html);
    }

    [Fact]
    public async Task Search_keeps_keyword_in_pagination_links()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        for (var i = 1; i <= 21; i++)
        {
            await SeedEventAsync(organizer, $"paging-{i:D2}", startAt: DateTime.Now.AddDays(i));
        }
        await SeedEventAsync(organizer, "不相關的活動");

        var page1 = await SearchAsync("paging");
        var page2 = await SearchAsync("paging", page: 2);

        var nextLink = System.Text.RegularExpressions.Regex.Match(page1, "href=\"([^\"]+)\"[^>]*>下一頁").Groups[1].Value;
        Assert.Contains("page=2", nextLink);
        Assert.Contains("q=paging", nextLink);
        Assert.Contains("paging-21", page2);
        Assert.DoesNotContain("不相關的活動", page1 + page2);
    }

    [Fact]
    public async Task Blank_search_shows_all_events()
    {
        await ClearEventsAsync();
        var organizer = await CreateOrganizerAsync();
        await SeedEventAsync(organizer, "任何活動");

        var html = await SearchAsync("   ");

        Assert.Contains("任何活動", html);
        Assert.Contains("近期活動", html);
    }

    // ---------------- 活動詳情 ----------------

    [Fact]
    public async Task Details_shows_published_event_to_anonymous_visitor()
    {
        var organizer = await CreateOrganizerAsync("好玩工作室");
        var id = await SeedEventAsync(organizer, "陶藝體驗課", capacity: 10, reservedCount: 3, price: 1200);

        var response = await NewClient().GetAsync($"/Events/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("陶藝體驗課", html);
        Assert.Contains("好玩工作室", html);
        Assert.Contains("NT$ 1,200", html);
        Assert.Contains("剩 7 名", html);
        Assert.Contains("報名中", html);
    }

    [Fact]
    public async Task Details_of_missing_event_returns_404()
    {
        var response = await NewClient().GetAsync("/Events/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Draft_is_hidden_from_anonymous_and_other_users()
    {
        var organizer = await CreateOrganizerAsync();
        var id = await SeedEventAsync(organizer, "還在準備的活動", EventStatus.Draft);

        var otherClient = NewClient();
        await otherClient.RegisterAsync(TestClientExtensions.NewEmail(), "Organizer");

        Assert.Equal(HttpStatusCode.NotFound, (await NewClient().GetAsync($"/Events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/Events/{id}")).StatusCode);
    }

    [Fact]
    public async Task Owner_can_preview_own_draft()
    {
        var ownerClient = NewClient();
        var email = TestClientExtensions.NewEmail();
        await ownerClient.RegisterAsync(email, "Organizer");
        string ownerId;
        using (var scope = factory.Services.CreateScope())
        {
            ownerId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
        }
        var id = await SeedEventAsync(ownerId, "我的草稿", EventStatus.Draft);

        var response = await ownerClient.GetAsync($"/Events/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("預覽模式", html);
        Assert.Contains("編輯此活動", html);
    }

    [Fact]
    public async Task Cancelled_event_is_still_viewable_and_marked_cancelled()
    {
        var organizer = await CreateOrganizerAsync();
        var id = await SeedEventAsync(organizer, "被取消的講座", EventStatus.Cancelled);

        var response = await NewClient().GetAsync($"/Events/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("活動已取消", html);
        Assert.DoesNotContain("報名功能即將開放", html);
    }

    [Fact]
    public async Task Full_event_shows_full_and_no_register_button()
    {
        var organizer = await CreateOrganizerAsync();
        var id = await SeedEventAsync(organizer, "熱門活動", capacity: 3, reservedCount: 3);

        var html = await NewClient().GetStringAsync($"/Events/{id}");

        Assert.Contains("已額滿", html);
        Assert.DoesNotContain("報名功能即將開放", html);
    }

    [Fact]
    public async Task Description_is_html_encoded()
    {
        var organizer = await CreateOrganizerAsync();
        var id = await SeedEventAsync(organizer, "XSS 測試", description: "<script>alert('x')</script>");

        var html = await NewClient().GetStringAsync($"/Events/{id}");

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
