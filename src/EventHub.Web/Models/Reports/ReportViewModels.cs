using EventHub.Web.Data.Entities;
using EventHub.Web.Services;

namespace EventHub.Web.Models.Reports;

/// <summary>金額彙總。只計算「已付款」的訂單（待付款、已取消、已退款都不算收款）。</summary>
public class AmountSummary
{
    /// <summary>已付款筆數（含免費報名）</summary>
    public int PaidCount { get; init; }

    /// <summary>實收 = 報名者付的錢</summary>
    public int Amount { get; init; }

    /// <summary>平台手續費</summary>
    public int PlatformFee { get; init; }

    /// <summary>應撥給主辦方 = 實收 − 手續費</summary>
    public int OrganizerAmount { get; init; }

    public static AmountSummary Empty { get; } = new();
}

// ---------------- 活動報名名單（主辦方、管理員共用） ----------------

/// <summary>報名名單的篩選條件</summary>
public enum RegistrationFilter
{
    /// <summary>有效報名：報名成功 + 待付款（佔用名額中的）</summary>
    Active,
    Paid,
    Pending,
    Cancelled,
    Refunded,
    All,
}

public static class RegistrationFilterExtensions
{
    public static string ToLabel(this RegistrationFilter f) => f switch
    {
        RegistrationFilter.Active => "有效報名",
        RegistrationFilter.Paid => "報名成功",
        RegistrationFilter.Pending => "待付款",
        RegistrationFilter.Cancelled => "已取消",
        RegistrationFilter.Refunded => "已退款",
        RegistrationFilter.All => "全部",
        _ => f.ToString(),
    };

    /// <summary>篩選條件包含的訂單狀態</summary>
    public static OrderStatus[] ToStatuses(this RegistrationFilter f) => f switch
    {
        RegistrationFilter.Active => [OrderStatus.Paid, OrderStatus.Pending],
        RegistrationFilter.Paid => [OrderStatus.Paid],
        RegistrationFilter.Pending => [OrderStatus.Pending],
        RegistrationFilter.Cancelled => [OrderStatus.Cancelled],
        RegistrationFilter.Refunded => [OrderStatus.Refunded],
        _ => Enum.GetValues<OrderStatus>(),
    };
}

/// <summary>各狀態的訂單筆數</summary>
public class RegistrationCounts
{
    public int Paid { get; init; }
    public int Pending { get; init; }
    public int Cancelled { get; init; }
    public int Refunded { get; init; }

    public int Get(RegistrationFilter f) => f switch
    {
        RegistrationFilter.Active => Paid + Pending,
        RegistrationFilter.Paid => Paid,
        RegistrationFilter.Pending => Pending,
        RegistrationFilter.Cancelled => Cancelled,
        RegistrationFilter.Refunded => Refunded,
        _ => Paid + Pending + Cancelled + Refunded,
    };
}

/// <summary>某活動的報名名單與金額</summary>
public class EventRegistrationsViewModel
{
    public int EventId { get; init; }
    public string Title { get; init; } = "";
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public string Location { get; init; } = "";
    public EventStatus EventStatus { get; init; }
    public int Price { get; init; }
    public int Capacity { get; init; }
    public int ReservedCount { get; init; }
    public string OrganizerName { get; init; } = "";

    public RegistrationFilter Filter { get; init; }
    public required RegistrationCounts Counts { get; init; }
    public required AmountSummary Amounts { get; init; }
    public IReadOnlyList<RegistrationRowViewModel> Rows { get; init; } = [];

    /// <summary>管理員看的版本：訂單編號連到管理員的訂單詳情</summary>
    public bool IsAdminView { get; init; }

    public bool IsFree => Price == 0;

    /// <summary>活動已取消，但還有已付款（付費）的訂單沒退款</summary>
    public bool HasOrdersToRefund => EventStatus == EventStatus.Cancelled && !IsFree && Counts.Paid > 0;
}

/// <summary>報名名單的一筆</summary>
public class RegistrationRowViewModel
{
    public string OrderNo { get; init; } = "";
    public string AttendeeName { get; init; } = "";
    public string AttendeePhone { get; init; } = "";
    public string AttendeeEmail { get; init; } = "";
    public string? Note { get; init; }
    public int Amount { get; init; }
    public int PlatformFee { get; init; }
    public int OrganizerAmount { get; init; }
    public OrderDisplayStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PaidAt { get; init; }
}

// ---------------- 管理員：活動總覽 ----------------

public class AdminEventListViewModel
{
    public string? Keyword { get; init; }
    public EventStatus? Status { get; init; }

    /// <summary>只看某位主辦方的活動（從撥款彙總點進來）</summary>
    public string? OrganizerId { get; init; }
    public string? OrganizerName { get; init; }

    public int Page { get; init; } = 1;
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage => Page > 1;
    public IReadOnlyList<AdminEventListItemViewModel> Items { get; init; } = [];
}

public class AdminEventListItemViewModel
{
    public int EventId { get; init; }
    public string Title { get; init; } = "";
    public DateTime StartAt { get; init; }
    public EventStatus Status { get; init; }
    public string OrganizerName { get; init; } = "";
    public int Price { get; init; }
    public int Capacity { get; init; }
    public int ReservedCount { get; init; }
    public required AmountSummary Amounts { get; init; }
}

// ---------------- 管理員：訂單列表 ----------------

public class AdminOrderListViewModel
{
    public string? Keyword { get; init; }
    public OrderStatus? Status { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage => Page > 1;

    /// <summary>符合條件的總筆數</summary>
    public int TotalCount { get; init; }

    public IReadOnlyList<AdminOrderListItemViewModel> Items { get; init; } = [];
}

public class AdminOrderListItemViewModel
{
    public string OrderNo { get; init; } = "";
    public int EventId { get; init; }
    public string EventTitle { get; init; } = "";
    public string AttendeeName { get; init; } = "";
    public string AttendeeEmail { get; init; } = "";
    public int Amount { get; init; }
    public OrderDisplayStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
}

// ---------------- 撥款彙總（每位主辦方） ----------------

/// <summary>
/// 某位主辦方的撥款彙總，只算付費的已付款訂單：
/// - 已結束活動：活動已結束且沒有取消，可以撥款
/// - 未結束活動：活動還沒結束，暫不撥款（可能還會取消、退款）
/// - 待退款：活動已取消，款項要退給報名者，不撥給主辦方
/// </summary>
public class OrganizerPayoutViewModel
{
    public string OrganizerId { get; init; } = "";
    public string OrganizerName { get; init; } = "";
    public string OrganizerEmail { get; init; } = "";

    public required AmountSummary Ended { get; init; }
    public required AmountSummary Upcoming { get; init; }
    public required AmountSummary ToRefund { get; init; }

    /// <summary>實收合計（不含待退款）</summary>
    public int TotalAmount => Ended.Amount + Upcoming.Amount;

    public int TotalPlatformFee => Ended.PlatformFee + Upcoming.PlatformFee;

    public int TotalOrganizerAmount => Ended.OrganizerAmount + Upcoming.OrganizerAmount;

    public int TotalPaidCount => Ended.PaidCount + Upcoming.PaidCount;
}

public class PayoutListViewModel
{
    public IReadOnlyList<OrganizerPayoutViewModel> Items { get; init; } = [];

    public int EndedOrganizerAmount => Items.Sum(i => i.Ended.OrganizerAmount);
    public int UpcomingOrganizerAmount => Items.Sum(i => i.Upcoming.OrganizerAmount);
    public int TotalPlatformFee => Items.Sum(i => i.TotalPlatformFee);
    public int ToRefundAmount => Items.Sum(i => i.ToRefund.Amount);
}
