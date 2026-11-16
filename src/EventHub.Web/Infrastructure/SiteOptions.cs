namespace EventHub.Web.Infrastructure;

/// <summary>網站本身的設定，對應 appsettings 的 "Site" 區段。</summary>
public class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// 網站對外網址（不含結尾斜線），用來在信件裡放「查看報名明細」的連結，
    /// 例如 https://eventhub.example.com。沒設定就不放連結。
    /// </summary>
    public string BaseUrl { get; set; } = "";
}
