using EventHub.Web.Services;

namespace EventHub.Web.Models.Events;

/// <summary>活動列表上的一張卡片。</summary>
public class EventCardViewModel
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public string Location { get; init; } = "";
    public int Price { get; init; }
    public string? CoverImagePath { get; init; }
    public int RemainingSeats { get; init; }
    public RegistrationAvailability Availability { get; init; }
}

public class EventListViewModel
{
    public required IReadOnlyList<EventCardViewModel> Items { get; init; }
    public int Page { get; init; }
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage { get; init; }

    /// <summary>目前的搜尋關鍵字（已整理過）；null = 沒有搜尋</summary>
    public string? Keyword { get; init; }
}
