using EventHub.Web.Data.Entities;

namespace EventHub.Web.Services;

/// <summary>活動目前的報名狀態（依時間、名額、活動狀態計算出來，不存資料庫）。</summary>
public enum RegistrationAvailability
{
    /// <summary>可以報名</summary>
    Open,

    /// <summary>名額已滿</summary>
    Full,

    /// <summary>已過報名截止時間（沒設截止時間則以活動開始時間為準），活動尚未結束</summary>
    Closed,

    /// <summary>活動已結束</summary>
    Ended,

    /// <summary>活動已取消</summary>
    Cancelled,

    /// <summary>草稿，尚未上架</summary>
    NotPublished,
}

/// <summary>
/// 判斷活動能不能報名。活動頁面顯示與報名時的檢查都用這裡，確保規則一致。
/// 注意：這只是「當下」的判斷；實際佔名額時仍要用條件式 UPDATE，避免兩人同時搶到最後一位。
/// </summary>
public static class EventAvailability
{
    public static RegistrationAvailability Evaluate(Event e, DateTime now) =>
        Evaluate(e.Status, e.StartAt, e.EndAt, e.RegistrationDeadline, e.Capacity, e.ReservedCount, now);

    public static RegistrationAvailability Evaluate(
        EventStatus status,
        DateTime startAt,
        DateTime endAt,
        DateTime? registrationDeadline,
        int capacity,
        int reservedCount,
        DateTime now)
    {
        if (status == EventStatus.Cancelled)
        {
            return RegistrationAvailability.Cancelled;
        }

        if (status != EventStatus.Published)
        {
            return RegistrationAvailability.NotPublished;
        }

        if (endAt <= now)
        {
            return RegistrationAvailability.Ended;
        }

        if (now >= (registrationDeadline ?? startAt))
        {
            return RegistrationAvailability.Closed;
        }

        if (reservedCount >= capacity)
        {
            return RegistrationAvailability.Full;
        }

        return RegistrationAvailability.Open;
    }

    public static string ToLabel(this RegistrationAvailability a) => a switch
    {
        RegistrationAvailability.Open => "報名中",
        RegistrationAvailability.Full => "已額滿",
        RegistrationAvailability.Closed => "報名已截止",
        RegistrationAvailability.Ended => "活動已結束",
        RegistrationAvailability.Cancelled => "活動已取消",
        RegistrationAvailability.NotPublished => "尚未上架",
        _ => a.ToString(),
    };

    /// <summary>Bootstrap badge 的顏色 class</summary>
    public static string ToBadgeClass(this RegistrationAvailability a) => a switch
    {
        RegistrationAvailability.Open => "text-bg-success",
        RegistrationAvailability.Full => "text-bg-warning",
        RegistrationAvailability.Cancelled => "text-bg-danger",
        _ => "text-bg-secondary",
    };
}
