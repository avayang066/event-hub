namespace EventHub.Web.Data.Entities;

/// <summary>
/// 活動。時間欄位一律存台灣時間（UTC+8），平台只服務台灣。
/// </summary>
public class Event
{
    public int Id { get; set; }

    /// <summary>主辦方（users.Id）</summary>
    public string OrganizerId { get; set; } = "";
    public ApplicationUser Organizer { get; set; } = null!;

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }

    /// <summary>報名截止時間；null 表示活動開始前都可報名</summary>
    public DateTime? RegistrationDeadline { get; set; }

    public string Location { get; set; } = "";

    /// <summary>報名費（新台幣，整數元；綠界金額只收整數）。0 = 免費</summary>
    public int Price { get; set; }

    /// <summary>名額上限</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// 已佔用名額（含待付款保留中的）。
    /// 報名時用單一條件式 UPDATE 遞增：
    ///   UPDATE events SET reserved_count = reserved_count + 1
    ///   WHERE id = @id AND reserved_count &lt; capacity
    /// 影響 0 筆就代表額滿，避免兩人同時搶到最後一個名額。
    /// </summary>
    public int ReservedCount { get; set; }

    /// <summary>封面圖片路徑（相對於 wwwroot，例如 /uploads/events/xxx.jpg）</summary>
    public string? CoverImagePath { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Draft;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
