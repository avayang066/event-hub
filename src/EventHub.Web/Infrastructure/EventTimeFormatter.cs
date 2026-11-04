namespace EventHub.Web.Infrastructure;

/// <summary>活動時間的顯示格式（以目前語系 zh-TW 顯示星期，例如「週六」）。</summary>
public static class EventTimeFormatter
{
    /// <summary>2026/12/05 (週六) 14:00</summary>
    public static string FormatDateTime(DateTime value) => value.ToString("yyyy/MM/dd (ddd) HH:mm");

    /// <summary>
    /// 同一天：2026/12/05 (週六) 14:00 – 17:00
    /// 跨日：  2026/12/05 (週六) 14:00 – 2026/12/06 (週日) 12:00
    /// </summary>
    public static string FormatRange(DateTime start, DateTime end) =>
        start.Date == end.Date
            ? $"{FormatDateTime(start)} – {end:HH:mm}"
            : $"{FormatDateTime(start)} – {FormatDateTime(end)}";
}
