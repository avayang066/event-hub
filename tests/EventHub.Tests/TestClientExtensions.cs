using System.Net;
using System.Text.RegularExpressions;

namespace EventHub.Tests;

/// <summary>整合測試共用：送出含防偽權杖（AntiForgery）的表單、註冊、登入。</summary>
internal static partial class TestClientExtensions
{
    public const string DefaultPassword = "Passw0rd123";

    /// <summary>先 GET 表單頁取得防偽權杖，再 POST 表單。</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string formPageUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(formPageUrl);
        var html = await page.Content.ReadAsStringAsync();

        var match = AntiForgeryTokenRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"在 {formPageUrl} 找不到防偽權杖");
        }

        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value),
        };

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email, string accountType, string password = DefaultPassword) =>
        client.PostFormAsync("/Account/Register", "/Account/Register", new()
        {
            ["AccountType"] = accountType,
            ["DisplayName"] = "測試使用者",
            ["Email"] = email,
            ["Password"] = password,
            ["ConfirmPassword"] = password,
        });

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password = DefaultPassword) =>
        client.PostFormAsync("/Account/Login", "/Account/Login", new()
        {
            ["Email"] = email,
            ["Password"] = password,
        });

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@test.local";

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiForgeryTokenRegex();
}
