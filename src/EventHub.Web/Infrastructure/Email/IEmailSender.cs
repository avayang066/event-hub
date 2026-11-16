namespace EventHub.Web.Infrastructure.Email;

/// <summary>一封要寄出的信。</summary>
public record EmailMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody);

/// <summary>寄信。正式環境用 SMTP（SmtpEmailSender），測試時換成假的寄信器。</summary>
public interface IEmailSender
{
    /// <summary>寄信設定是否完整（沒設定就不寄，也不算失敗）</summary>
    bool IsConfigured { get; }

    /// <summary>寄出；失敗時丟出例外</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
