namespace EventHub.Web.Infrastructure;

/// <summary>
/// 平台時間一律用台灣時間（UTC+8，無日光節約）。
/// 透過 TimeProvider 取得時間，測試時可以替換成固定時間。
/// </summary>
public static class TaiwanTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static DateTime GetTaiwanNow(this TimeProvider timeProvider) =>
        DateTime.SpecifyKind(timeProvider.GetUtcNow().UtcDateTime + Offset, DateTimeKind.Unspecified);
}
