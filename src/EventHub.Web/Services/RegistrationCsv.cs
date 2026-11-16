using System.Text;
using EventHub.Web.Models.Reports;

namespace EventHub.Web.Services;

/// <summary>報名名單匯出成 CSV（Excel 可直接開啟）。</summary>
public static class RegistrationCsv
{
    /// <summary>UTF-8 加上 BOM，Excel 才會正確顯示中文</summary>
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static byte[] Build(EventRegistrationsViewModel model)
    {
        var sb = new StringBuilder();
        AppendRow(sb, "訂單編號", "姓名", "手機", "Email", "備註", "狀態", "金額", "平台手續費", "主辦方實拿", "報名時間", "付款時間");

        foreach (var r in model.Rows)
        {
            AppendRow(sb,
                r.OrderNo,
                r.AttendeeName,
                r.AttendeePhone,
                r.AttendeeEmail,
                r.Note ?? "",
                r.Status.ToLabel(),
                r.Amount.ToString(),
                r.PlatformFee.ToString(),
                r.OrganizerAmount.ToString(),
                r.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                r.PaidAt?.ToString("yyyy-MM-dd HH:mm") ?? "");
        }

        var preamble = Utf8WithBom.GetPreamble();
        var body = Utf8WithBom.GetBytes(sb.ToString());
        return [.. preamble, .. body];
    }

    /// <summary>下載檔名，例如 event-12-registrations-20261116.csv</summary>
    public static string FileName(EventRegistrationsViewModel model, DateTime now) =>
        $"event-{model.EventId}-registrations-{now:yyyyMMdd}.csv";

    private static void AppendRow(StringBuilder sb, params string[] fields)
    {
        sb.AppendJoin(',', fields.Select(Escape));
        sb.Append("\r\n");
    }

    /// <summary>
    /// CSV 欄位跳脫：
    /// - 含逗號、引號、換行就用雙引號包起來，引號重複兩次
    /// - 以 = + - @ 開頭的文字前面加 '，避免 Excel 當成公式執行（CSV injection）。
    ///   姓名、備註是報名者自己填的，不能信任。
    /// </summary>
    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
