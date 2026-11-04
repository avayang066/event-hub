using Microsoft.Extensions.Options;

namespace EventHub.Web.Infrastructure;

/// <summary>
/// 活動封面圖片：驗證、儲存、刪除。
/// 檔名一律用隨機 GUID，不使用使用者上傳的原始檔名。
/// </summary>
public class EventImageStorage(
    IOptions<UploadOptions> options,
    IWebHostEnvironment env,
    ILogger<EventImageStorage> logger)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    private const string PublicPrefix = "/uploads/events/";

    private string EventsFolder =>
        Path.Combine(
            string.IsNullOrEmpty(options.Value.RootPath)
                ? Path.Combine(env.WebRootPath, "uploads")
                : options.Value.RootPath,
            "events");

    /// <summary>
    /// 檢查是否為允許的圖片（JPG / PNG / WebP，5MB 以內）。
    /// 依檔案內容開頭的特徵碼判斷，不相信副檔名與瀏覽器送來的 Content-Type。
    /// </summary>
    /// <returns>錯誤訊息；null 代表通過，並由 <paramref name="extension"/> 回傳副檔名</returns>
    public static string? Validate(IFormFile file, out string extension)
    {
        extension = "";

        if (file.Length == 0)
        {
            return "圖片檔案是空的。";
        }

        if (file.Length > MaxBytes)
        {
            return "圖片不能超過 5MB。";
        }

        Span<byte> header = stackalloc byte[12];
        using (var stream = file.OpenReadStream())
        {
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            header = header[..read];
        }

        var detected = DetectExtension(header);
        if (detected is null)
        {
            return "只接受 JPG、PNG、WebP 格式的圖片。";
        }

        extension = detected;
        return null;
    }

    /// <summary>儲存圖片，回傳網址路徑，例如 /uploads/events/xxxx.jpg</summary>
    public async Task<string> SaveAsync(IFormFile file, string extension, CancellationToken ct = default)
    {
        Directory.CreateDirectory(EventsFolder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(EventsFolder, fileName);

        await using (var target = new FileStream(fullPath, FileMode.CreateNew))
        {
            await file.CopyToAsync(target, ct);
        }

        return PublicPrefix + fileName;
    }

    /// <summary>刪除先前儲存的圖片；路徑不是本系統產生的就忽略。刪除失敗只記錄，不中斷流程。</summary>
    public void Delete(string? publicPath)
    {
        if (string.IsNullOrEmpty(publicPath) || !publicPath.StartsWith(PublicPrefix, StringComparison.Ordinal))
        {
            return;
        }

        // 只取檔名，避免路徑中有 ../ 刪到其他檔案
        var fullPath = Path.Combine(EventsFolder, Path.GetFileName(publicPath));
        try
        {
            File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "刪除活動圖片失敗 {Path}", fullPath);
        }
    }

    private static string? DetectExtension(ReadOnlySpan<byte> h)
    {
        // JPEG: FF D8 FF
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
        {
            return ".jpg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (h.Length >= 8 && h[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        // WebP: "RIFF" ???? "WEBP"
        if (h.Length >= 12
            && h[..4].SequenceEqual("RIFF"u8)
            && h[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }
}
