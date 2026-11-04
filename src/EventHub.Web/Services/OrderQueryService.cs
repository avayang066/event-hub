using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Orders;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Web.Services;

/// <summary>報名者查詢自己的訂單。所有查詢都限定 userId，看不到別人的訂單。</summary>
public class OrderQueryService(AppDbContext db, TimeProvider timeProvider)
{
    /// <summary>
    /// 我的報名。不列出已經沒有意義的訂單：
    /// - 本人取消或逾期被取消的（已取消，且活動沒有取消）
    /// - 已超過付款期限、等待排程取消的待付款
    /// 因「活動取消」而被取消的訂單仍列出，讓報名者知道活動取消了。
    /// 這些訂單仍可從網址直接查看，只是不出現在列表。
    /// </summary>
    public async Task<List<OrderListItemViewModel>> ListAsync(string userId)
    {
        var now = timeProvider.GetTaiwanNow();

        var rows = await db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Where(o => o.Status != OrderStatus.Cancelled || o.Event.Status == EventStatus.Cancelled)
            .Where(o => !(o.Status == OrderStatus.Pending && o.ExpiresAt <= now))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderNo,
                o.Event.Title,
                o.Event.StartAt,
                o.Amount,
                o.Status,
                o.ExpiresAt,
                o.CreatedAt,
            })
            .ToListAsync();

        return rows.Select(o => new OrderListItemViewModel
        {
            OrderNo = o.OrderNo,
            EventTitle = o.Title,
            EventStartAt = o.StartAt,
            Amount = o.Amount,
            Status = OrderStatusDisplay.Get(o.Status, o.ExpiresAt, now),
            CreatedAt = o.CreatedAt,
        }).ToList();
    }

    public async Task<OrderDetailViewModel?> GetAsync(string orderNo, string userId)
    {
        var o = await db.Orders
            .AsNoTracking()
            .Include(x => x.Event)
            .FirstOrDefaultAsync(x => x.OrderNo == orderNo && x.UserId == userId);

        return o is null ? null : ToDetail(o);
    }

    /// <summary>管理員查詢任何一筆訂單。</summary>
    public async Task<AdminOrderDetailViewModel?> GetForAdminAsync(string orderNo)
    {
        var o = await db.Orders
            .AsNoTracking()
            .Include(x => x.Event)
            .Include(x => x.User)
            .Include(x => x.Organizer)
            .FirstOrDefaultAsync(x => x.OrderNo == orderNo);

        if (o is null)
        {
            return null;
        }

        return new AdminOrderDetailViewModel
        {
            Order = ToDetail(o),
            UserEmail = o.User.Email ?? "",
            OrganizerName = o.Organizer.DisplayName ?? o.Organizer.Email ?? "",
            PlatformFee = o.PlatformFee,
            OrganizerAmount = o.OrganizerAmount,
            EcpayTradeNo = o.EcpayTradeNo,
            PaymentType = o.PaymentType,
            RefundAmount = o.RefundAmount,
        };
    }

    private OrderDetailViewModel ToDetail(Data.Entities.Order o) =>
        new()
        {
            OrderNo = o.OrderNo,
            Status = OrderStatusDisplay.Get(o.Status, o.ExpiresAt, timeProvider.GetTaiwanNow()),
            EventId = o.EventId,
            EventTitle = o.Event.Title,
            EventStartAt = o.Event.StartAt,
            EventEndAt = o.Event.EndAt,
            EventLocation = o.Event.Location,
            EventCancelled = o.Event.Status == Data.Entities.EventStatus.Cancelled,
            AttendeeName = o.AttendeeName,
            AttendeePhone = o.AttendeePhone,
            AttendeeEmail = o.AttendeeEmail,
            Note = o.Note,
            Amount = o.Amount,
            CreatedAt = o.CreatedAt,
            ExpiresAt = o.ExpiresAt,
            PaidAt = o.PaidAt,
            CancelledAt = o.CancelledAt,
            RefundedAt = o.RefundedAt,
        };
}
