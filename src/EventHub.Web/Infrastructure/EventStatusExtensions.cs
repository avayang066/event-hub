using EventHub.Web.Data.Entities;

namespace EventHub.Web.Infrastructure;

public static class EventStatusExtensions
{
    public static string ToLabel(this EventStatus status) => status switch
    {
        EventStatus.Draft => "草稿",
        EventStatus.Published => "已上架",
        EventStatus.Cancelled => "已取消",
        _ => status.ToString(),
    };

    /// <summary>Bootstrap badge 的顏色 class</summary>
    public static string ToBadgeClass(this EventStatus status) => status switch
    {
        EventStatus.Draft => "text-bg-secondary",
        EventStatus.Published => "text-bg-success",
        EventStatus.Cancelled => "text-bg-danger",
        _ => "text-bg-light",
    };

    /// <summary>報名費顯示：0 顯示「免費」，其餘 NT$ 1,200</summary>
    public static string ToPriceLabel(this int price) =>
        price == 0 ? "免費" : $"NT$ {price:N0}";
}
