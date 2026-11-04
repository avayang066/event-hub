using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Registration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventHub.Web.Services;

/// <summary>
/// 報名：佔名額、防止重複報名、建立訂單。
///
/// 名額控制的核心是「條件式 UPDATE」：
///   UPDATE events SET reserved_count = reserved_count + 1
///   WHERE id = @id AND reserved_count &lt; capacity AND ...
/// 資料庫保證同一時間只有一個交易能把最後一個名額加上去，另一個交易更新 0 筆就代表額滿。
/// 佔名額、重複檢查、建立訂單在同一個交易內，任何一步失敗都整個復原。
/// </summary>
public class RegistrationService(
    AppDbContext db,
    TimeProvider timeProvider,
    IOptions<EcpayOptions> ecpayOptions,
    ILogger<RegistrationService> logger)
{
    /// <summary>待付款保留名額的時間</summary>
    public static readonly TimeSpan PaymentHoldTime = TimeSpan.FromMinutes(15);

    public record RegisterResult(OperationResult Result, string? OrderNo, bool AlreadyRegistered)
    {
        public static RegisterResult Fail(string message) => new(OperationResult.Fail(message), null, false);
        public static RegisterResult Existing(string orderNo) => new(OperationResult.Success(), orderNo, true);
        public static RegisterResult Created(string orderNo) => new(OperationResult.Success(), orderNo, false);
    }

    private DateTime Now => timeProvider.GetTaiwanNow();

    /// <summary>找可以顯示報名頁的活動（草稿視為不存在）。</summary>
    public Task<Event?> FindEventAsync(int eventId) =>
        db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.Status != EventStatus.Draft);

    /// <summary>
    /// 此人在此活動「有效」的訂單：已付款，或待付款且尚未超過付款期限。
    /// 有的話就不能再報名。
    /// </summary>
    public Task<string?> FindActiveOrderNoAsync(int eventId, string userId)
    {
        var now = Now;
        return db.Orders
            .Where(o => o.EventId == eventId && o.UserId == userId
                && (o.Status == OrderStatus.Paid || (o.Status == OrderStatus.Pending && o.ExpiresAt > now)))
            .Select(o => o.OrderNo)
            .FirstOrDefaultAsync();
    }

    public static string? GetUnavailableMessage(RegistrationAvailability availability) => availability switch
    {
        RegistrationAvailability.Open => null,
        RegistrationAvailability.Full => "很抱歉，名額已滿。",
        RegistrationAvailability.Closed => "報名已截止。",
        RegistrationAvailability.Ended => "活動已結束。",
        RegistrationAvailability.Cancelled => "活動已取消。",
        _ => "活動尚未開放報名。",
    };

    public async Task<RegisterResult> RegisterAsync(int eventId, string userId, RegistrationFormViewModel form)
    {
        var now = Now;

        // ---- 1. 先用目前資料快速檢查，給出清楚的訊息（不是最終判斷）----
        var e = await FindEventAsync(eventId);
        if (e is null)
        {
            return RegisterResult.Fail("找不到這個活動。");
        }

        // 「額滿」不在這裡擋：名額可能被此人自己逾期未付款的訂單佔住，
        // 第 2 步釋放後就有位子；是否真的額滿交給第 3 步的條件式 UPDATE 決定
        var availability = EventAvailability.Evaluate(e, now);
        if (availability is not RegistrationAvailability.Open and not RegistrationAvailability.Full)
        {
            return RegisterResult.Fail(GetUnavailableMessage(availability)!);
        }

        if (await FindActiveOrderNoAsync(eventId, userId) is { } existingOrderNo)
        {
            return RegisterResult.Existing(existingOrderNo);
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        // ---- 鎖住這個活動的資料列：同一活動的報名從這裡開始排隊，直到交易結束 ----
        // （值不變的 UPDATE 仍會取得資料列鎖。先鎖活動再動 orders，
        //   避免多人同時報名時在 orders 表上互相等待造成 deadlock）
        await db.Events
            .Where(x => x.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedCount, x => x.ReservedCount));

        // ---- 2. 此人在此活動若有逾期未付款的訂單，先取消並釋放名額 ----
        // （平常由排程處理；這裡處理是為了讓本人可以立刻重新報名）
        var expiredCount = await db.Orders
            .Where(o => o.EventId == eventId && o.UserId == userId
                && o.Status == OrderStatus.Pending && o.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelledAt, now)
                .SetProperty(o => o.UpdatedAt, now));

        if (expiredCount > 0)
        {
            await db.Events
                .Where(x => x.Id == eventId && x.ReservedCount >= expiredCount)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedCount, x => x.ReservedCount - expiredCount));
        }

        // ---- 3. 佔名額（條件式 UPDATE，這一步才是最終判斷）----
        var seatTaken = await db.Events
            .Where(x => x.Id == eventId
                && x.Status == EventStatus.Published
                && x.ReservedCount < x.Capacity
                && x.EndAt > now
                && (x.RegistrationDeadline ?? x.StartAt) > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedCount, x => x.ReservedCount + 1));

        if (seatTaken == 0)
        {
            // 交易未提交，離開時自動復原（包含第 2 步）
            var latest = await db.Events.AsNoTracking().FirstAsync(x => x.Id == eventId);
            return RegisterResult.Fail(
                GetUnavailableMessage(EventAvailability.Evaluate(latest, now)) ?? "很抱歉，名額已滿。");
        }

        // ---- 4. 重複報名檢查 ----
        // 交易開頭已鎖住這個活動的資料列，同一活動的報名在此依序執行，
        // 同一人連按兩次送出也只會有一筆成功。
        if (await FindActiveOrderNoAsync(eventId, userId) is { } duplicateOrderNo)
        {
            return RegisterResult.Existing(duplicateOrderNo);
        }

        // ---- 5. 建立訂單 ----
        var amount = e.Price;
        var platformFee = (int)Math.Round(amount * ecpayOptions.Value.PlatformFeeRate, MidpointRounding.AwayFromZero);
        var isFree = amount == 0;

        var order = new Order
        {
            OrderNo = OrderNumberGenerator.Generate(now),
            EventId = eventId,
            UserId = userId,
            OrganizerId = e.OrganizerId,
            AttendeeName = form.AttendeeName.Trim(),
            AttendeePhone = NormalizePhone(form.AttendeePhone),
            AttendeeEmail = form.AttendeeEmail.Trim(),
            Note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim(),
            Amount = amount,
            PlatformFee = platformFee,
            OrganizerAmount = amount - platformFee,
            // 免費活動不用付款，直接報名成功
            Status = isFree ? OrderStatus.Paid : OrderStatus.Pending,
            PaidAt = isFree ? now : null,
            ExpiresAt = isFree ? null : now + PaymentHoldTime,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        logger.LogInformation("報名成功 訂單 {OrderNo} 活動 {EventId} 會員 {UserId} 金額 {Amount}",
            order.OrderNo, eventId, userId, amount);

        return RegisterResult.Created(order.OrderNo);
    }

    /// <summary>0912-345-678 → 0912345678</summary>
    private static string NormalizePhone(string phone) => phone.Replace("-", "").Trim();
}
