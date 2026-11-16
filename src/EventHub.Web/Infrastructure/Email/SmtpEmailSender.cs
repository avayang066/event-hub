using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventHub.Web.Infrastructure.Email;

/// <summary>
/// 透過 SMTP 寄信（MailKit）。
/// 開發環境預設寄到本機的 Mailpit（localhost:1025），信不會真的寄出，
/// 打開 http://localhost:8025 就能看到。
/// </summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    private SmtpOptions Options => options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Options.Host) && !string.IsNullOrWhiteSpace(Options.FromAddress);

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(Options.FromName, Options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 30_000 };

        // UseStartTls=true：一般寄信服務的 587 port；false：Mailpit 等本機伺服器（465 port 會自動用 SSL）
        var security = Options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(Options.Host, Options.Port, security, ct);

        if (!string.IsNullOrEmpty(Options.UserName))
        {
            await client.AuthenticateAsync(Options.UserName, Options.Password ?? "", ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
