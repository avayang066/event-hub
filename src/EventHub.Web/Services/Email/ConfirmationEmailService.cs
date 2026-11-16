using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventHub.Web.Services.Email;

/// <summary>
/// 寄出報名成功信。
///
/// 避免重複寄信：寄信前先用條件式 UPDATE 把 orders.confirmation_email_sent_at 填上時間
/// （只有原本是 NULL 的才會成功），同一張訂單同時被觸發多次也只有一個會真的寄出。
/// 寄信失敗就把時間清回 NULL，由背景排程稍後重寄。
/// </summary>
public class ConfirmationEmailService(
    AppDbContext db,
    IEmailSender emailSender,
    IOptions<SiteOptions> siteOptions,
    TimeProvider timeProvider,
    ILogger<ConfirmationEmailService> logger)
{
    /// <summary>重寄排程只補寄付款後這段時間內的訂單（太舊的就不寄了）</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromHours(24);

    public enum SendOutcome
    {
        /// <summary>這次寄出</summary>
        Sent,

        /// <summary>之前已經寄過（或另一個執行緒正在寄）</summary>
        AlreadySent,

        /// <summary>不需要寄：找不到訂單、未付款、已取消／退款、活動已取消</summary>
        NotEligible,

        /// <summary>寄信設定不完整，沒有寄</summary>
        NotConfigured,

        /// <summary>寄信失敗，稍後重寄</summary>
        Failed,
    }

    public async Task<SendOutcome> SendAsync(string orderNo, CancellationToken ct = default)
    {
        if (!emailSender.IsConfigured)
        {
            logger.LogWarning("寄信設定（Smtp 區段）不完整，未寄出訂單 {OrderNo} 的報名成功信", orderNo);
            return SendOutcome.NotConfigured;
        }

        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderNo == orderNo)
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.ConfirmationEmailSentAt,
                EventCancelled = o.Event.Status == EventStatus.Cancelled,
                Data = new ConfirmationEmailData(
                    o.OrderNo,
                    o.AttendeeName,
                    o.AttendeeEmail,
                    o.Event.Title,
                    o.Event.StartAt,
                    o.Event.EndAt,
                    o.Event.Location,
                    o.Amount),
            })
            .FirstOrDefaultAsync(ct);

        if (order is null || order.Status != OrderStatus.Paid || order.EventCancelled)
        {
            return SendOutcome.NotEligible;
        }

        if (order.ConfirmationEmailSentAt is not null)
        {
            return SendOutcome.AlreadySent;
        }

        // 先登記「已寄出」，搶到的才寄（同時被觸發多次只寄一封）
        var now = timeProvider.GetTaiwanNow();
        var claimed = await db.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.Paid && o.ConfirmationEmailSentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConfirmationEmailSentAt, now), ct);

        if (claimed == 0)
        {
            return SendOutcome.AlreadySent;
        }

        try
        {
            var message = ConfirmationEmailTemplate.Build(order.Data, siteOptions.Value.BaseUrl);
            await emailSender.SendAsync(message, ct);

            logger.LogInformation("已寄出報名成功信 訂單 {OrderNo}", orderNo);
            return SendOutcome.Sent;
        }
        catch (Exception ex)
        {
            // 清回 NULL，讓排程重寄（網站關閉中也要清，所以不用 ct）
            await db.Orders
                .Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ConfirmationEmailSentAt, (DateTime?)null),
                    CancellationToken.None);

            if (ex is OperationCanceledException && ct.IsCancellationRequested)
            {
                throw;
            }

            logger.LogError(ex, "寄送報名成功信失敗 訂單 {OrderNo}，稍後重寄", orderNo);
            return SendOutcome.Failed;
        }
    }

    /// <summary>
    /// 補寄：找出已付款、還沒寄信、付款時間在 RetryWindow 內的訂單逐一寄出
    /// （寄信失敗或網站在寄信前重啟的訂單）。
    /// </summary>
    /// <returns>這次寄出的封數</returns>
    public async Task<int> SendPendingAsync(CancellationToken ct = default)
    {
        if (!emailSender.IsConfigured)
        {
            return 0;
        }

        var since = timeProvider.GetTaiwanNow() - RetryWindow;
        var orderNos = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid
                && o.ConfirmationEmailSentAt == null
                && o.PaidAt >= since
                && o.Event.Status != EventStatus.Cancelled)
            .OrderBy(o => o.PaidAt)
            .Select(o => o.OrderNo)
            .Take(50)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var orderNo in orderNos)
        {
            if (await SendAsync(orderNo, ct) == SendOutcome.Sent)
            {
                sent++;
            }
        }

        return sent;
    }
}
