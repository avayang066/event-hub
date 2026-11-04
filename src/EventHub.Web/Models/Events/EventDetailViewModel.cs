using EventHub.Web.Services;

namespace EventHub.Web.Models.Events;

/// <summary>活動詳情頁。</summary>
public class EventDetailViewModel
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public DateTime? RegistrationDeadline { get; init; }
    public string Location { get; init; } = "";
    public int Price { get; init; }
    public int Capacity { get; init; }
    public int RemainingSeats { get; init; }
    public string? CoverImagePath { get; init; }
    public string OrganizerName { get; init; } = "";
    public RegistrationAvailability Availability { get; init; }

    /// <summary>目前瀏覽者就是這個活動的主辦方</summary>
    public bool IsOwner { get; init; }

    /// <summary>目前瀏覽者在此活動的有效訂單編號（已付款或待付款未逾期）；沒有則為 null</summary>
    public string? MyOrderNo { get; init; }

    /// <summary>主辦方預覽自己尚未上架的草稿</summary>
    public bool IsDraftPreview => Availability == RegistrationAvailability.NotPublished;

    public string MapUrl => "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(Location);
}
