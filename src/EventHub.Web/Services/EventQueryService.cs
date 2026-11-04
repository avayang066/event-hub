using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Web.Services;

/// <summary>前台活動查詢：活動列表、活動詳情。</summary>
public class EventQueryService(AppDbContext db, TimeProvider timeProvider)
{
    public const int PageSize = 20;

    public const int MaxKeywordLength = 50;

    /// <summary>
    /// 已上架、尚未結束的活動，依開始時間由近到遠排序。
    /// 報名截止或額滿的活動仍會列出（標示狀態），直到活動結束。
    /// </summary>
    /// <param name="keyword">搜尋標題（部分符合，英文不分大小寫）；空白 = 不篩選</param>
    public async Task<EventListViewModel> ListUpcomingAsync(int page, string? keyword = null)
    {
        page = Math.Max(1, page);
        keyword = NormalizeKeyword(keyword);
        var now = timeProvider.GetTaiwanNow();

        var query = db.Events
            .AsNoTracking()
            .Where(e => e.Status == EventStatus.Published && e.EndAt > now);

        if (keyword is not null)
        {
            var pattern = $"%{EscapeLikePattern(keyword)}%";
            query = query.Where(e => EF.Functions.Like(e.Title, pattern, LikeEscapeChar));
        }

        // 多取一筆，用來判斷有沒有下一頁
        var rows = await query
            .OrderBy(e => e.StartAt)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize + 1)
            .ToListAsync();

        var items = rows
            .Take(PageSize)
            .Select(e => new EventCardViewModel
            {
                Id = e.Id,
                Title = e.Title,
                StartAt = e.StartAt,
                EndAt = e.EndAt,
                Location = e.Location,
                Price = e.Price,
                CoverImagePath = e.CoverImagePath,
                RemainingSeats = Math.Max(0, e.Capacity - e.ReservedCount),
                Availability = EventAvailability.Evaluate(e, now),
            })
            .ToList();

        return new EventListViewModel
        {
            Items = items,
            Page = page,
            HasNextPage = rows.Count > PageSize,
            Keyword = keyword,
        };
    }

    private const string LikeEscapeChar = "\\";

    /// <summary>去頭尾空白、限制長度；空字串視為沒有搜尋。</summary>
    private static string? NormalizeKeyword(string? keyword)
    {
        keyword = keyword?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return null;
        }

        return keyword.Length > MaxKeywordLength ? keyword[..MaxKeywordLength] : keyword;
    }

    /// <summary>
    /// 跳脫 LIKE 的特殊字元，讓使用者輸入的 % 和 _ 被當成一般文字，
    /// 否則搜尋「100%」會變成萬用字元比對。
    /// </summary>
    private static string EscapeLikePattern(string value) =>
        value
            .Replace(LikeEscapeChar, LikeEscapeChar + LikeEscapeChar)
            .Replace("%", LikeEscapeChar + "%")
            .Replace("_", LikeEscapeChar + "_");

    /// <summary>
    /// 活動詳情。已上架、已取消的活動任何人都看得到（已報名的人可能從舊連結進來）；
    /// 草稿只有主辦方本人能預覽，其他人一律當作不存在。
    /// </summary>
    public async Task<EventDetailViewModel?> GetDetailAsync(int id, string? viewerUserId)
    {
        var e = await db.Events
            .AsNoTracking()
            .Include(x => x.Organizer)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (e is null)
        {
            return null;
        }

        var isOwner = viewerUserId is not null && e.OrganizerId == viewerUserId;
        if (e.Status == EventStatus.Draft && !isOwner)
        {
            return null;
        }

        var now = timeProvider.GetTaiwanNow();
        string? myOrderNo = null;
        if (viewerUserId is not null)
        {
            myOrderNo = await db.Orders
                .Where(o => o.EventId == id && o.UserId == viewerUserId
                    && (o.Status == OrderStatus.Paid || (o.Status == OrderStatus.Pending && o.ExpiresAt > now)))
                .Select(o => o.OrderNo)
                .FirstOrDefaultAsync();
        }

        return new EventDetailViewModel
        {
            Id = e.Id,
            Title = e.Title,
            Description = e.Description,
            StartAt = e.StartAt,
            EndAt = e.EndAt,
            RegistrationDeadline = e.RegistrationDeadline,
            Location = e.Location,
            Price = e.Price,
            Capacity = e.Capacity,
            RemainingSeats = Math.Max(0, e.Capacity - e.ReservedCount),
            CoverImagePath = e.CoverImagePath,
            OrganizerName = string.IsNullOrWhiteSpace(e.Organizer.DisplayName) ? "主辦單位" : e.Organizer.DisplayName,
            Availability = EventAvailability.Evaluate(e, now),
            IsOwner = isOwner,
            MyOrderNo = myOrderNo,
        };
    }
}
