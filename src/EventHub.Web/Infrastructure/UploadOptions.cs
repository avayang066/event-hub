namespace EventHub.Web.Infrastructure;

/// <summary>上傳檔案設定，對應 appsettings 的 "Uploads" 區段。</summary>
public class UploadOptions
{
    public const string SectionName = "Uploads";

    /// <summary>
    /// 上傳檔案存放的實體資料夾。留空 = wwwroot/uploads（網址為 /uploads/...）。
    /// </summary>
    public string RootPath { get; set; } = "";
}
