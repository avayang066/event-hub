using System.Threading.Channels;

namespace EventHub.Web.Services.Email;

/// <summary>
/// 待寄的報名成功信（記憶體佇列，存訂單編號）。
/// 報名成功或付款成功時只把訂單編號放進來就立刻返回，不必等寄信完成，
/// 綠界付款通知才能馬上回 1|OK；實際寄信由 ConfirmationEmailBackgroundService 處理。
/// 網站重啟時佇列會清空，沒寄到的由背景排程的補寄找回來。
/// </summary>
public class ConfirmationEmailQueue
{
    private readonly Channel<string> _channel =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(string orderNo) => _channel.Writer.TryWrite(orderNo);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
