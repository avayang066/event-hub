namespace EventHub.Web.Infrastructure;

/// <summary>
/// 平台管理員帳號，對應 appsettings 的 "AdminAccount" 區段。
/// 網站啟動時若此 Email 尚無帳號，就用這組密碼建立並設為 Admin。
/// Email 留空則不建立。正式環境請用環境變數或 user-secrets 設定，不要寫進 repo。
/// </summary>
public class AdminAccountOptions
{
    public const string SectionName = "AdminAccount";

    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "平台管理員";
}
