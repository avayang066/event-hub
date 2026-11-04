namespace EventHub.Web.Data.Entities;

/// <summary>活動狀態（資料庫以字串儲存）</summary>
public enum EventStatus
{
    /// <summary>草稿：只有主辦方看得到</summary>
    Draft,

    /// <summary>已上架：出現在活動列表、可報名</summary>
    Published,

    /// <summary>已取消</summary>
    Cancelled,
}
