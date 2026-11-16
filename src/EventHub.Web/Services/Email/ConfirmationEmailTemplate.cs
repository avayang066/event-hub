using System.Net;
using EventHub.Web.Infrastructure;
using EventHub.Web.Infrastructure.Email;

namespace EventHub.Web.Services.Email;

/// <summary>報名成功信要用到的資料。</summary>
public record ConfirmationEmailData(
    string OrderNo,
    string AttendeeName,
    string AttendeeEmail,
    string EventTitle,
    DateTime EventStartAt,
    DateTime EventEndAt,
    string EventLocation,
    int Amount);

/// <summary>
/// 報名成功信的內容（HTML 與純文字兩種版本）。
/// 使用者輸入的文字一律 HTML 編碼，避免信件內容被插入 HTML。
/// 信件用 inline style，因為多數收信軟體不支援 &lt;style&gt;。
/// </summary>
public static class ConfirmationEmailTemplate
{
    public static EmailMessage Build(ConfirmationEmailData d, string? siteBaseUrl)
    {
        var time = EventTimeFormatter.FormatRange(d.EventStartAt, d.EventEndAt);
        var amount = d.Amount == 0 ? "免費" : $"{d.Amount.ToPriceLabel()}（已付款）";
        var detailUrl = string.IsNullOrWhiteSpace(siteBaseUrl)
            ? null
            : $"{siteBaseUrl.TrimEnd('/')}/Orders/{d.OrderNo}";

        var subject = $"【EventHub】報名成功：{d.EventTitle}";

        return new EmailMessage(
            d.AttendeeEmail,
            d.AttendeeName,
            subject,
            BuildHtml(d, time, amount, detailUrl),
            BuildText(d, time, amount, detailUrl));
    }

    private static string BuildHtml(ConfirmationEmailData d, string time, string amount, string? detailUrl)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);

        static string Row(string label, string value) => $"""
            <tr>
              <td style="padding:8px 0;color:#8a7f73;width:88px;vertical-align:top;">{label}</td>
              <td style="padding:8px 0;color:#3d352e;">{value}</td>
            </tr>
            """;

        var button = detailUrl is null
            ? ""
            : $"""
              <p style="margin:28px 0 0;">
                <a href="{E(detailUrl)}" style="display:inline-block;background:#c0673f;color:#ffffff;text-decoration:none;padding:12px 24px;border-radius:8px;">查看報名明細</a>
              </p>
              """;

        return $"""
            <!DOCTYPE html>
            <html lang="zh-Hant">
            <body style="margin:0;padding:24px;background:#faf7f2;font-family:'Microsoft JhengHei','PingFang TC',sans-serif;">
              <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #eee5da;border-radius:12px;padding:32px;">
                <p style="margin:0 0 4px;color:#c0673f;font-weight:bold;">EventHub</p>
                <h1 style="margin:0 0 24px;font-size:22px;color:#3d352e;">報名成功</h1>
                <p style="margin:0 0 20px;color:#3d352e;line-height:1.7;">
                  {E(d.AttendeeName)} 您好，<br>
                  您已成功報名以下活動，活動當天見！
                </p>
                <table style="width:100%;border-collapse:collapse;border-top:1px solid #eee5da;border-bottom:1px solid #eee5da;">
                  {Row("活動", $"<strong>{E(d.EventTitle)}</strong>")}
                  {Row("時間", E(time))}
                  {Row("地點", E(d.EventLocation))}
                  {Row("金額", E(amount))}
                  {Row("訂單編號", $"<span style=\"font-family:Consolas,monospace;\">{E(d.OrderNo)}</span>")}
                </table>
                {button}
                <p style="margin:28px 0 0;font-size:13px;color:#8a7f73;line-height:1.7;">
                  如需取消或有任何問題，請登入 EventHub 查看報名明細或聯絡主辦單位。<br>
                  此信件由系統自動發送，請勿直接回覆。
                </p>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildText(ConfirmationEmailData d, string time, string amount, string? detailUrl)
    {
        var link = detailUrl is null ? "" : $"\n查看報名明細：{detailUrl}\n";

        return $"""
            {d.AttendeeName} 您好，

            您已成功報名以下活動，活動當天見！

            活動：{d.EventTitle}
            時間：{time}
            地點：{d.EventLocation}
            金額：{amount}
            訂單編號：{d.OrderNo}
            {link}
            如需取消或有任何問題，請登入 EventHub 查看報名明細或聯絡主辦單位。
            此信件由系統自動發送，請勿直接回覆。

            EventHub
            """;
    }
}
