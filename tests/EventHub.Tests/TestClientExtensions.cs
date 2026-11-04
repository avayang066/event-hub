using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace EventHub.Tests;

/// <summary>整合測試共用：送出含防偽權杖（AntiForgery）的表單、註冊、登入。</summary>
internal static partial class TestClientExtensions
{
    public const string DefaultPassword = "Passw0rd123";

    /// <summary>GET 表單頁，取出頁面上的防偽權杖。</summary>
    public static async Task<string> GetAntiForgeryTokenAsync(this HttpClient client, string formPageUrl)
    {
        var page = await client.GetAsync(formPageUrl);
        var html = await page.Content.ReadAsStringAsync();

        var match = AntiForgeryTokenRegex().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"在 {formPageUrl} 找不到防偽權杖（HTTP {(int)page.StatusCode}）");
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>先 GET 表單頁取得防偽權杖，再 POST 一般表單。</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string formPageUrl, string postUrl, Dictionary<string, string> fields)
    {
        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = await client.GetAntiForgeryTokenAsync(formPageUrl),
        };

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    /// <summary>先 GET 表單頁取得防偽權杖，再 POST multipart 表單（可附檔案）。</summary>
    public static async Task<HttpResponseMessage> PostMultipartAsync(
        this HttpClient client, string formPageUrl, string postUrl,
        Dictionary<string, string> fields, TestFile? file = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(await client.GetAntiForgeryTokenAsync(formPageUrl)), "__RequestVerificationToken" },
        };

        foreach (var (name, value) in fields)
        {
            content.Add(new StringContent(value), name);
        }

        if (file is not null)
        {
            var fileContent = new ByteArrayContent(file.Content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, file.FieldName, file.FileName);
        }

        return await client.PostAsync(postUrl, content);
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

/// <summary>測試上傳用的檔案。</summary>
internal record TestFile(string FieldName, string FileName, string ContentType, byte[] Content)
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>開頭是 PNG 特徵碼的檔案，可指定總大小。</summary>
    public static TestFile Png(int totalBytes = 1024, string fieldName = "CoverImage")
    {
        var bytes = new byte[totalBytes];
        PngSignature.CopyTo(bytes, 0);
        return new TestFile(fieldName, "cover.png", "image/png", bytes);
    }

    /// <summary>副檔名與 Content-Type 假裝成圖片，內容其實是文字。</summary>
    public static TestFile FakeJpg(string fieldName = "CoverImage") =>
        new(fieldName, "virus.jpg", "image/jpeg", "<?php echo 'hi'; ?>"u8.ToArray());
}
