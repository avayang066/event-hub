using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Web.Services;

/// <summary>
/// 訂單狀態轉換。所有改變訂單狀態的地方都必須經過這裡，規則只有一份：
///
///   待付款 ──付款成功──► 已付款 ──退款──► 已退款
///     │
///     └──報名者取消／逾期未付款／活動取消──► 已取消
///
/// - 每次轉換都是「條件式 UPDATE」：只有在目前狀態符合時才會更新，
///   同時發生的兩個轉換（例如付款成功與逾期取消）只會有一個成功。
/// - 進入已取消／已退款時，名額還給活動（同一交易內）。
/// - 交易一開始先鎖活動資料列，再動 orders，和報名流程的上鎖順序一致，避免 deadlock。
/// </summary>
public class OrderStateService(
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<OrderStateService> logger)
{
    public enum MarkPaidOutcome
    {
        /// <summary>這次把訂單改為已付款</summary>
        Paid,

        /// <summary>之前就已經是已付款（重複通知）</summary>
        AlreadyPaid,

        /// <summary>找不到訂單</summary>
        NotFound,

        /// <summary>訂單已取消或已退款，不能改為已付款（需要人工處理退款）</summary>
        NotPayable,
    }

    private DateTime Now => timeProvider.GetTaiwanNow();

    // ---------------- 待付款 → 已付款 ----------------

    /// <summary>
    /// 付款成功（由綠界付款結果通知呼叫）。
    /// 已超過付款期限但尚未被排程取消的訂單仍接受付款：錢已經付了，名額也還保留著。
    /// </summary>
    public async Task<MarkPaidOutcome> MarkPaidAsync(string orderNo, string? ecpayTradeNo, string? paymentType)
    {
        var now = Now;

        var updated = await db.Orders
            .Where(o => o.OrderNo == orderNo && o.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, now)
                .SetProperty(o => o.EcpayTradeNo, ecpayTradeNo)
                .SetProperty(o => o.PaymentType, paymentType)
                .SetProperty(o => o.UpdatedAt, now));

        if (updated == 1)
        {
            logger.LogInformation("訂單 {OrderNo} 已付款（綠界交易 {TradeNo}）", orderNo, ecpayTradeNo);
            return MarkPaidOutcome.Paid;
        }

        var status = await db.Orders
            .Where(o => o.OrderNo == orderNo)
            .Select(o => (OrderStatus?)o.Status)
            .FirstOrDefaultAsync();

        return status switch
        {
            null => MarkPaidOutcome.NotFound,
            OrderStatus.Paid => MarkPaidOutcome.AlreadyPaid,
            _ => MarkPaidOutcome.NotPayable,
        };
    }

    // ---------------- 待付款 → 已取消 ----------------

    /// <summary>報名者取消自己的待付款訂單。</summary>
    public async Task<OperationResult> CancelByAttendeeAsync(string orderNo, string userId)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderNo == orderNo && o.UserId == userId)
            .Select(o => new { o.Id, o.EventId, o.Status })
            .FirstOrDefaultAsync();

        if (order is null)
        {
            return OperationResult.Fail("找不到這筆報名。");
        }

        if (order.Status != OrderStatus.Pending)
        {
            return OperationResult.Fail(order.Status == OrderStatus.Paid
                ? "已付款的報名無法自行取消，如需退款請聯絡主辦單位。"
                : "這筆報名已經取消了。");
        }

        var cancelled = await CancelPendingAsync(order.EventId, o => o.Id == order.Id);
        if (cancelled == 0)
        {
            // 同一時間付款成功或被排程取消
            return OperationResult.Fail("訂單狀態已變更，請重新整理後再試。");
        }

        logger.LogInformation("報名者取消訂單 {OrderNo}", orderNo);
        return OperationResult.Success();
    }

    /// <summary>
    /// 取消所有超過付款期限的待付款訂單並釋放名額（背景排程每分鐘呼叫）。
    /// </summary>
    /// <returns>取消的訂單數</returns>
    public async Task<int> CancelExpiredAsync(CancellationToken ct = default)
    {
        var now = Now;

        var eventIds = await db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.ExpiresAt <= now)
            .Select(o => o.EventId)
            .Distinct()
            .ToListAsync(ct);

        var total = 0;
        foreach (var eventId in eventIds)
        {
            ct.ThrowIfCancellationRequested();
            total += await CancelPendingAsync(eventId, o => o.ExpiresAt <= now);
        }

        if (total > 0)
        {
            logger.LogInformation("已取消 {Count} 筆逾期未付款訂單", total);
        }

        return total;
    }

    /// <summary>活動取消時，取消該活動所有待付款訂單（已付款的保留，等待退款）。</summary>
    /// <returns>取消的訂單數</returns>
    public async Task<int> CancelPendingForEventAsync(int eventId)
    {
        var count = await CancelPendingAsync(eventId, _ => true);
        if (count > 0)
        {
            logger.LogInformation("活動 {EventId} 取消，連帶取消 {Count} 筆待付款訂單", eventId, count);
        }

        return count;
    }

    // ---------------- 已付款 → 已退款 ----------------

    /// <summary>
    /// 管理員在綠界後台完成退款後，標記訂單為已退款（目前只支援全額退款）。
    /// </summary>
    public async Task<OperationResult> MarkRefundedAsync(string orderNo, string adminUserId)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderNo == orderNo)
            .Select(o => new { o.Id, o.EventId, o.Status, o.Amount })
            .FirstOrDefaultAsync();

        if (order is null)
        {
            return OperationResult.Fail("找不到這筆訂單。");
        }

        if (order.Status != OrderStatus.Paid)
        {
            return OperationResult.Fail("只有已付款的訂單可以標記為已退款。");
        }

        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockEventAsync(order.EventId);

        var updated = await db.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Refunded)
                .SetProperty(o => o.RefundedAt, now)
                .SetProperty(o => o.RefundAmount, order.Amount)
                .SetProperty(o => o.UpdatedAt, now));

        if (updated == 0)
        {
            return OperationResult.Fail("訂單狀態已變更，請重新整理後再試。");
        }

        await ReleaseSeatsAsync(order.EventId, 1);
        await tx.CommitAsync();

        logger.LogInformation("管理員 {AdminId} 將訂單 {OrderNo} 標記為已退款（{Amount} 元）",
            adminUserId, orderNo, order.Amount);
        return OperationResult.Success();
    }

    // ---------------- 共用 ----------------

    /// <summary>
    /// 在一個交易內：鎖活動 → 取消符合條件的待付款訂單 → 釋放對應名額。
    /// </summary>
    private async Task<int> CancelPendingAsync(
        int eventId, System.Linq.Expressions.Expression<Func<Order, bool>> extraCondition)
    {
        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockEventAsync(eventId);

        var cancelled = await db.Orders
            .Where(o => o.EventId == eventId && o.Status == OrderStatus.Pending)
            .Where(extraCondition)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.UpdatedAt, now));

        if (cancelled > 0)
        {
            await ReleaseSeatsAsync(eventId, cancelled);
        }

        await tx.CommitAsync();
        return cancelled;
    }

    /// <summary>取得活動資料列鎖（值不變的 UPDATE）。</summary>
    private Task LockEventAsync(int eventId) =>
        db.Events
            .Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReservedCount, e => e.ReservedCount));

    private async Task ReleaseSeatsAsync(int eventId, int count)
    {
        var released = await db.Events
            .Where(e => e.Id == eventId && e.ReservedCount >= count)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReservedCount, e => e.ReservedCount - count));

        if (released == 0)
        {
            // 理論上不會發生；發生代表 reserved_count 與訂單不一致，需要人工檢查
            logger.LogError("釋放名額失敗：活動 {EventId} 的 reserved_count 小於 {Count}", eventId, count);
        }
    }
}
