using EventHub.Web.Data.Entities;

namespace EventHub.Web.Services;

/// <summary>
/// 給使用者看的訂單狀態。和資料庫的 OrderStatus 差在「待付款但已超過付款期限」會另外顯示為付款逾期
/// （逾期訂單由排程取消並釋放名額之前，資料庫裡仍是 Pending）。
/// </summary>
public enum OrderDisplayStatus
{
    AwaitingPayment,
    PaymentExpired,
    Paid,
    Cancelled,
    Refunded,
}

public static class OrderStatusDisplay
{
    public static OrderDisplayStatus Get(OrderStatus status, DateTime? expiresAt, DateTime now) => status switch
    {
        OrderStatus.Pending when expiresAt is { } deadline && deadline <= now => OrderDisplayStatus.PaymentExpired,
        OrderStatus.Pending => OrderDisplayStatus.AwaitingPayment,
        OrderStatus.Paid => OrderDisplayStatus.Paid,
        OrderStatus.Cancelled => OrderDisplayStatus.Cancelled,
        OrderStatus.Refunded => OrderDisplayStatus.Refunded,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static string ToLabel(this OrderDisplayStatus s) => s switch
    {
        OrderDisplayStatus.AwaitingPayment => "待付款",
        OrderDisplayStatus.PaymentExpired => "付款逾期",
        OrderDisplayStatus.Paid => "報名成功",
        OrderDisplayStatus.Cancelled => "已取消",
        OrderDisplayStatus.Refunded => "已退款",
        _ => s.ToString(),
    };

    public static string ToBadgeClass(this OrderDisplayStatus s) => s switch
    {
        OrderDisplayStatus.AwaitingPayment => "text-bg-warning",
        OrderDisplayStatus.Paid => "text-bg-success",
        OrderDisplayStatus.Refunded => "text-bg-info",
        _ => "text-bg-secondary",
    };
}
