namespace EventHub.Web.Infrastructure;

/// <summary>寄信設定（MailKit），對應 appsettings 的 "Smtp" 區段。</summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "EventHub";
}
