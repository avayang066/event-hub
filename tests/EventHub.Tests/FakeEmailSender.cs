using System.Collections.Concurrent;
using EventHub.Web.Infrastructure.Email;

namespace EventHub.Tests;

/// <summary>測試用的寄信器：不真的寄出，只記錄寄了哪些信；可以設定讓它寄信失敗。</summary>
public class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public bool IsConfigured => true;

    /// <summary>寄給這些收件人的信會失敗（模擬 SMTP 伺服器連不上）</summary>
    public ConcurrentDictionary<string, bool> FailingRecipients { get; } = new();

    public IReadOnlyList<EmailMessage> SentTo(string address) =>
        _sent.Where(m => m.ToAddress == address).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (FailingRecipients.ContainsKey(message.ToAddress))
        {
            throw new InvalidOperationException("模擬寄信失敗");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>等待寄給某人的信出現（信是背景寄出的，不會馬上就有）。</summary>
    public async Task<EmailMessage> WaitForAsync(string address, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (SentTo(address).FirstOrDefault() is { } message)
            {
                return message;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"等不到寄給 {address} 的信");
    }
}
