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
    public bool EventCancelled { get; init; }

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

    /// <summary>待付款（含已逾期但尚未被排程取消）的訂單，報名者可以自行取消</summary>
    public bool CanCancel => Status is OrderDisplayStatus.AwaitingPayment or OrderDisplayStatus.PaymentExpired;
}

/// <summary>管理員看的訂單詳情：多了會員、主辦方、金額拆分與綠界交易資訊。</summary>
public class AdminOrderDetailViewModel
{
    public required OrderDetailViewModel Order { get; init; }
    public string UserEmail { get; init; } = "";
    public string OrganizerName { get; init; } = "";
    public int PlatformFee { get; init; }
    public int OrganizerAmount { get; init; }
    public string? EcpayTradeNo { get; init; }
    public string? PaymentType { get; init; }
    public int? RefundAmount { get; init; }

    /// <summary>這張訂單收到的綠界付款通知（依收到時間排序）</summary>
    public IReadOnlyList<PaymentNotificationItemViewModel> PaymentNotifications { get; init; } = [];

    public bool CanMarkRefunded => Order.Status == OrderDisplayStatus.Paid;

    /// <summary>有重複付款、取消後才付款等需要人工處理的通知</summary>
    public bool HasPaymentIssue => PaymentNotifications.Any(n => PaymentProcessResult.NeedsAttention(n.ProcessResult));
}

/// <summary>一則綠界付款通知（管理員查看）。</summary>
public class PaymentNotificationItemViewModel
{
    public DateTime ReceivedAt { get; init; }
    public string MerchantTradeNo { get; init; } = "";
    public string? EcpayTradeNo { get; init; }
    public int TradeAmount { get; init; }
    public string? PaymentType { get; init; }
    public string ProcessResult { get; init; } = "";
}
