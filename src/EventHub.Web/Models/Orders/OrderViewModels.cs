using EventHub.Web.Services;

namespace EventHub.Web.Models.Orders;

/// <summary>我的報名列表的一筆。</summary>
public class OrderListItemViewModel
{
    public string OrderNo { get; init; } = "";
    public string EventTitle { get; init; } = "";
    public DateTime EventStartAt { get; init; }
    public int Amount { get; init; }
    public OrderDisplayStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>訂單詳情。</summary>
public class OrderDetailViewModel
{
    public string OrderNo { get; init; } = "";
    public OrderDisplayStatus Status { get; init; }

    public int EventId { get; init; }
    public string EventTitle { get; init; } = "";
    public DateTime EventStartAt { get; init; }
    public DateTime EventEndAt { get; init; }
    public string EventLocation { get; init; } = "";

    public string AttendeeName { get; init; } = "";
    public string AttendeePhone { get; init; } = "";
    public string AttendeeEmail { get; init; } = "";
    public string? Note { get; init; }

    public int Amount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public DateTime? RefundedAt { get; init; }

    public bool IsAwaitingPayment => Status == OrderDisplayStatus.AwaitingPayment;
}
