using EventHub.Web.Data;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Orders;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Web.Services;

/// <summary>報名者查詢自己的訂單。所有查詢都限定 userId，看不到別人的訂單。</summary>
public class OrderQueryService(AppDbContext db, TimeProvider timeProvider)
{
    public async Task<List<OrderListItemViewModel>> ListAsync(string userId)
    {
        var now = timeProvider.GetTaiwanNow();

        var rows = await db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
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

        if (o is null)
        {
            return null;
        }

        return new OrderDetailViewModel
        {
            OrderNo = o.OrderNo,
            Status = OrderStatusDisplay.Get(o.Status, o.ExpiresAt, timeProvider.GetTaiwanNow()),
            EventId = o.EventId,
            EventTitle = o.Event.Title,
            EventStartAt = o.Event.StartAt,
            EventEndAt = o.Event.EndAt,
            EventLocation = o.Event.Location,
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
}
