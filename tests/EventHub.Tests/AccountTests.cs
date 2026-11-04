using System.Net;
using EventHub.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

public class AccountTests(TestWebAppFactory factory) : IClassFixture<TestWebAppFactory>
{
    /// <summary>不自動跟隨轉址，才能檢查轉到哪裡；Cookie 會自動保留（維持登入狀態）。</summary>
    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByEmailAsync(email);
    }

    private async Task<IList<string>> GetRolesAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? Array.Empty<string>() : await userManager.GetRolesAsync(user);
    }

    // ---------------- 註冊與角色 ----------------

    [Theory]
    [InlineData("Organizer", AppRoles.Organizer)]
    [InlineData("Attendee", AppRoles.Attendee)]
    public async Task Register_assigns_selected_role_and_signs_in(string accountType, string expectedRole)
    {
        var client = NewClient();
        var email = TestClientExtensions.NewEmail();

        var response = await client.RegisterAsync(email, accountType);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(new[] { expectedRole }, await GetRolesAsync(email));

        // 註冊後直接是登入狀態：頁首出現登出按鈕
        var home = await client.GetStringAsync("/");
        Assert.Contains("登出", home);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("3")]
    [InlineData("")]
    public async Task Register_rejects_invalid_account_type(string accountType)
    {
        var client = NewClient();
        var email = TestClientExtensions.NewEmail();

        var response = await client.RegisterAsync(email, accountType);

        // 停留在註冊頁、身分欄位顯示錯誤，帳號沒有被建立
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("field-validation-error\"[^>]*data-valmsg-for=\"AccountType\"[^>]*>請選擇身分<", html);
        Assert.Null(await FindUserAsync(email));
    }

    [Fact]
    public async Task Register_rejects_duplicate_email()
    {
        var email = TestClientExtensions.NewEmail();
        await NewClient().RegisterAsync(email, "Attendee");

        var response = await NewClient().RegisterAsync(email, "Organizer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("已經註冊過了", await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { AppRoles.Attendee }, await GetRolesAsync(email));
    }

    [Theory]
    [InlineData("12345678", "密碼需包含英文字母")]
    [InlineData("abcdefgh", "密碼需包含數字")]
    public async Task Register_enforces_password_rules(string password, string expectedError)
    {
        var response = await NewClient().RegisterAsync(TestClientExtensions.NewEmail(), "Attendee", password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedError, await response.Content.ReadAsStringAsync());
    }

    // ---------------- 登入 ----------------

    [Fact]
    public async Task Login_with_correct_password_succeeds()
    {
        var email = TestClientExtensions.NewEmail();
        await NewClient().RegisterAsync(email, "Attendee");

        var client = NewClient();
        var response = await client.LoginAsync(email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("登出", await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_error()
    {
        var email = TestClientExtensions.NewEmail();
        await NewClient().RegisterAsync(email, "Attendee");

        var response = await NewClient().LoginAsync(email, "WrongPass999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email 或密碼不正確", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_locks_account_after_five_failures()
    {
        var email = TestClientExtensions.NewEmail();
        await NewClient().RegisterAsync(email, "Attendee");

        var client = NewClient();
        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync(email, "WrongPass999");
        }

        // 鎖定後即使密碼正確也不能登入
        var response = await client.LoginAsync(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("帳號已暫時鎖定", await response.Content.ReadAsStringAsync());
    }

    // ---------------- 權限 ----------------

    [Fact]
    public async Task Anonymous_user_is_redirected_to_login_for_organizer_page()
    {
        var response = await NewClient().GetAsync("/Organizer");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Attendee_cannot_access_organizer_page()
    {
        var client = NewClient();
        await client.RegisterAsync(TestClientExtensions.NewEmail(), "Attendee");

        var response = await client.GetAsync("/Organizer");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Organizer_can_access_organizer_page()
    {
        var client = NewClient();
        await client.RegisterAsync(TestClientExtensions.NewEmail(), "Organizer");

        var response = await client.GetAsync("/Organizer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("我的活動", await response.Content.ReadAsStringAsync());
    }
}
