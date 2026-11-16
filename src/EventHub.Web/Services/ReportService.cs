using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Reports;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Web.Services;

/// <summary>
/// 後台報表：活動報名名單、訂單列表、活動總覽、撥款彙總。只讀不寫。
/// 金額一律只算「已付款」的訂單；訂單上的手續費、應撥金額是報名當下的快照，直接加總即可。
/// </summary>
public class ReportService(AppDbContext db, TimeProvider timeProvider)
{
    public const int PageSize = 30;

    private DateTime Now => timeProvider.GetTaiwanNow();

    // ---------------- 活動報名名單 ----------------

    /// <param name="organizerId">有值時只找這位主辦方的活動（主辦方只能看自己的）；null = 管理員，不限</param>
    /// <returns>活動不存在（或不是自己的）回傳 null</returns>
    public async Task<EventRegistrationsViewModel?> GetEventRegistrationsAsync(
        int eventId, string? organizerId, RegistrationFilter filter, bool isAdminView)
    {
        var eventQuery = db.Events.AsNoTracking().Where(e => e.Id == eventId);
        if (organizerId is not null)
        {
            eventQuery = eventQuery.Where(e => e.OrganizerId == organizerId);
        }

        var ev = await eventQuery
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.StartAt,
                e.EndAt,
                e.Location,
                e.Status,
                e.Price,
                e.Capacity,
                e.ReservedCount,
                OrganizerName = e.Organizer.DisplayName ?? e.Organizer.Email ?? "",
            })
            .FirstOrDefaultAsync();

        if (ev is null)
        {
            return null;
        }

        var groups = await db.Orders
            .Where(o => o.EventId == eventId)
            .GroupBy(o => o.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Amount = g.Sum(o => o.Amount),
                PlatformFee = g.Sum(o => o.PlatformFee),
                OrganizerAmount = g.Sum(o => o.OrganizerAmount),
            })
            .ToListAsync();

        int CountOf(OrderStatus status) => groups.FirstOrDefault(g => g.Status == status)?.Count ?? 0;
        var paid = groups.FirstOrDefault(g => g.Status == OrderStatus.Paid);

        var now = Now;
        var rows = await FilterByStatus(db.Orders.AsNoTracking().Where(o => o.EventId == eventId), filter)
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Select(o => new
            {
                o.OrderNo,
                o.AttendeeName,
                o.AttendeePhone,
                o.AttendeeEmail,
                o.Note,
                o.Amount,
                o.PlatformFee,
                o.OrganizerAmount,
                o.Status,
                o.ExpiresAt,
                o.CreatedAt,
                o.PaidAt,
            })
            .ToListAsync();

        return new EventRegistrationsViewModel
        {
            EventId = ev.Id,
            Title = ev.Title,
            StartAt = ev.StartAt,
            EndAt = ev.EndAt,
            Location = ev.Location,
            EventStatus = ev.Status,
            Price = ev.Price,
            Capacity = ev.Capacity,
            ReservedCount = ev.ReservedCount,
            OrganizerName = ev.OrganizerName,
            Filter = filter,
            IsAdminView = isAdminView,
            Counts = new RegistrationCounts
            {
                Paid = CountOf(OrderStatus.Paid),
                Pending = CountOf(OrderStatus.Pending),
                Cancelled = CountOf(OrderStatus.Cancelled),
                Refunded = CountOf(OrderStatus.Refunded),
            },
            Amounts = paid is null
                ? AmountSummary.Empty
                : new AmountSummary
                {
                    PaidCount = paid.Count,
                    Amount = paid.Amount,
                    PlatformFee = paid.PlatformFee,
                    OrganizerAmount = paid.OrganizerAmount,
                },
            Rows = rows.Select(o => new RegistrationRowViewModel
            {
                OrderNo = o.OrderNo,
                AttendeeName = o.AttendeeName,
                AttendeePhone = o.AttendeePhone,
                AttendeeEmail = o.AttendeeEmail,
                Note = o.Note,
                Amount = o.Amount,
                PlatformFee = o.PlatformFee,
                OrganizerAmount = o.OrganizerAmount,
                Status = OrderStatusDisplay.Get(o.Status, o.ExpiresAt, now),
                CreatedAt = o.CreatedAt,
                PaidAt = o.PaidAt,
            }).ToList(),
        };
    }

    private static IQueryable<Order> FilterByStatus(IQueryable<Order> query, RegistrationFilter filter) => filter switch
    {
        RegistrationFilter.Active => query.Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.Pending),
        RegistrationFilter.Paid => query.Where(o => o.Status == OrderStatus.Paid),
        RegistrationFilter.Pending => query.Where(o => o.Status == OrderStatus.Pending),
        RegistrationFilter.Cancelled => query.Where(o => o.Status == OrderStatus.Cancelled),
        RegistrationFilter.Refunded => query.Where(o => o.Status == OrderStatus.Refunded),
        _ => query,
    };

    // ---------------- 管理員：活動總覽 ----------------

    /// <summary>所有活動（含草稿、已取消），依開始時間由新到舊。</summary>
    public async Task<AdminEventListViewModel> ListEventsAsync(
        string? keyword, EventStatus? status, string? organizerId, int page)
    {
        page = Math.Max(1, page);
        keyword = SearchKeyword.Normalize(keyword);
        organizerId = string.IsNullOrWhiteSpace(organizerId) ? null : organizerId;

        var query = db.Events.AsNoTracking();

        if (keyword is not null)
        {
            var pattern = SearchKeyword.ToContainsPattern(keyword);
            query = query.Where(e =>
                EF.Functions.Like(e.Title, pattern, SearchKeyword.LikeEscapeChar)
                || EF.Functions.Like(e.Organizer.DisplayName!, pattern, SearchKeyword.LikeEscapeChar)
                || EF.Functions.Like(e.Organizer.Email!, pattern, SearchKeyword.LikeEscapeChar));
        }

        if (status is not null)
        {
            query = query.Where(e => e.Status == status);
        }

        if (organizerId is not null)
        {
            query = query.Where(e => e.OrganizerId == organizerId);
        }

        // 多取一筆，用來判斷有沒有下一頁
        var events = await query
            .OrderByDescending(e => e.StartAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize + 1)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.StartAt,
                e.Status,
                e.Price,
                e.Capacity,
                e.ReservedCount,
                OrganizerName = e.Organizer.DisplayName ?? e.Organizer.Email ?? "",
            })
            .ToListAsync();

        var pageEvents = events.Take(PageSize).ToList();
        var eventIds = pageEvents.Select(e => e.Id).ToList();
        var amounts = await PaidAmountsByEventAsync(db.Orders.Where(o => eventIds.Contains(o.EventId)));

        string? organizerName = null;
        if (organizerId is not null)
        {
            organizerName = await db.Users
                .Where(u => u.Id == organizerId)
                .Select(u => u.DisplayName ?? u.Email)
                .FirstOrDefaultAsync();
        }

        return new AdminEventListViewModel
        {
            Keyword = keyword,
            Status = status,
            OrganizerId = organizerId,
            OrganizerName = organizerName,
            Page = page,
            HasNextPage = events.Count > PageSize,
            Items = pageEvents.Select(e => new AdminEventListItemViewModel
            {
                EventId = e.Id,
                Title = e.Title,
                StartAt = e.StartAt,
                Status = e.Status,
                OrganizerName = e.OrganizerName,
                Price = e.Price,
                Capacity = e.Capacity,
                ReservedCount = e.ReservedCount,
                Amounts = amounts.GetValueOrDefault(e.Id, AmountSummary.Empty),
            }).ToList(),
        };
    }

    /// <summary>依活動加總已付款訂單的金額</summary>
    private static async Task<Dictionary<int, AmountSummary>> PaidAmountsByEventAsync(IQueryable<Order> orders) =>
        await orders
            .Where(o => o.Status == OrderStatus.Paid)
            .GroupBy(o => o.EventId)
            .Select(g => new
            {
                EventId = g.Key,
                PaidCount = g.Count(),
                Amount = g.Sum(o => o.Amount),
                PlatformFee = g.Sum(o => o.PlatformFee),
                OrganizerAmount = g.Sum(o => o.OrganizerAmount),
            })
            .ToDictionaryAsync(x => x.EventId, x => new AmountSummary
            {
                PaidCount = x.PaidCount,
                Amount = x.Amount,
                PlatformFee = x.PlatformFee,
                OrganizerAmount = x.OrganizerAmount,
            });

    // ---------------- 管理員：訂單列表 ----------------

    /// <param name="keyword">訂單編號、報名者姓名、Email、手機（部分符合）</param>
    /// <param name="from">報名日期起（含）</param>
    /// <param name="to">報名日期迄（含當天）</param>
    public async Task<AdminOrderListViewModel> ListOrdersAsync(
        string? keyword, OrderStatus? status, DateTime? from, DateTime? to, int page)
    {
        page = Math.Max(1, page);
        keyword = SearchKeyword.Normalize(keyword);
        from = from?.Date;
        to = to?.Date;

        var query = db.Orders.AsNoTracking();

        if (keyword is not null)
        {
            var pattern = SearchKeyword.ToContainsPattern(keyword);
            query = query.Where(o =>
                EF.Functions.Like(o.OrderNo, pattern, SearchKeyword.LikeEscapeChar)
                || EF.Functions.Like(o.AttendeeName, pattern, SearchKeyword.LikeEscapeChar)
                || EF.Functions.Like(o.AttendeeEmail, pattern, SearchKeyword.LikeEscapeChar)
                || EF.Functions.Like(o.AttendeePhone, pattern, SearchKeyword.LikeEscapeChar));
        }

        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        if (from is not null)
        {
            query = query.Where(o => o.CreatedAt >= from);
        }

        if (to is not null)
        {
            var toExclusive = to.Value.AddDays(1);
            query = query.Where(o => o.CreatedAt < toExclusive);
        }

        var total = await query.CountAsync();

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize + 1)
            .Select(o => new
            {
                o.OrderNo,
                o.EventId,
                EventTitle = o.Event.Title,
                o.AttendeeName,
                o.AttendeeEmail,
                o.Amount,
                o.Status,
                o.ExpiresAt,
                o.CreatedAt,
            })
            .ToListAsync();

        var now = Now;
        return new AdminOrderListViewModel
        {
            Keyword = keyword,
            Status = status,
            From = from,
            To = to,
            Page = page,
            HasNextPage = rows.Count > PageSize,
            TotalCount = total,
            Items = rows.Take(PageSize).Select(o => new AdminOrderListItemViewModel
            {
                OrderNo = o.OrderNo,
                EventId = o.EventId,
                EventTitle = o.EventTitle,
                AttendeeName = o.AttendeeName,
                AttendeeEmail = o.AttendeeEmail,
                Amount = o.Amount,
                Status = OrderStatusDisplay.Get(o.Status, o.ExpiresAt, now),
                CreatedAt = o.CreatedAt,
            }).ToList(),
        };
    }

    // ---------------- 撥款彙總 ----------------

    /// <summary>
    /// 每位主辦方的撥款彙總（只算付費的已付款訂單），依應撥金額由多到少。
    /// </summary>
    /// <param name="organizerId">有值時只算這位主辦方</param>
    public async Task<List<OrganizerPayoutViewModel>> GetPayoutsAsync(string? organizerId = null)
    {
        var orders = db.Orders.Where(o => o.Amount > 0);
        if (organizerId is not null)
        {
            orders = orders.Where(o => o.OrganizerId == organizerId);
        }

        var byEvent = await PaidAmountsByEventAsync(orders);
        if (byEvent.Count == 0)
        {
            return [];
        }

        var eventIds = byEvent.Keys.ToList();
        var events = await db.Events
            .Where(e => eventIds.Contains(e.Id))
            .Select(e => new { e.Id, e.OrganizerId, e.Status, e.EndAt })
            .ToListAsync();

        var organizerIds = events.Select(e => e.OrganizerId).Distinct().ToList();
        var organizers = await db.Users
            .Where(u => organizerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToDictionaryAsync(u => u.Id);

        var now = Now;
        return events
            .GroupBy(e => e.OrganizerId)
            .Select(g =>
            {
                var organizer = organizers.GetValueOrDefault(g.Key);
                return new OrganizerPayoutViewModel
                {
                    OrganizerId = g.Key,
                    OrganizerName = organizer?.DisplayName ?? organizer?.Email ?? g.Key,
                    OrganizerEmail = organizer?.Email ?? "",
                    ToRefund = Sum(g.Where(e => e.Status == EventStatus.Cancelled).Select(e => byEvent[e.Id])),
                    Ended = Sum(g.Where(e => e.Status != EventStatus.Cancelled && e.EndAt <= now).Select(e => byEvent[e.Id])),
                    Upcoming = Sum(g.Where(e => e.Status != EventStatus.Cancelled && e.EndAt > now).Select(e => byEvent[e.Id])),
                };
            })
            .OrderByDescending(p => p.Ended.OrganizerAmount)
            .ThenByDescending(p => p.TotalOrganizerAmount)
            .ThenBy(p => p.OrganizerName)
            .ToList();
    }

    private static AmountSummary Sum(IEnumerable<AmountSummary> items)
    {
        int count = 0, amount = 0, fee = 0, organizerAmount = 0;
        foreach (var x in items)
        {
            count += x.PaidCount;
            amount += x.Amount;
            fee += x.PlatformFee;
            organizerAmount += x.OrganizerAmount;
        }

        return new AmountSummary { PaidCount = count, Amount = amount, PlatformFee = fee, OrganizerAmount = organizerAmount };
    }
}
