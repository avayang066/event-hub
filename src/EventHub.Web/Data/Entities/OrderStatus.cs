namespace EventHub.Web.Data.Entities;

/// <summary>訂單狀態（資料庫以字串儲存）</summary>
public enum OrderStatus
{
    /// <summary>待付款：已保留名額，等待綠界付款結果；逾期 (ExpiresAt) 自動取消並釋放名額</summary>
    Pending,

    /// <summary>已付款（免費活動報名後直接為此狀態）</summary>
    Paid,

    /// <summary>已取消：逾期未付款或使用者取消，名額已釋放</summary>
    Cancelled,

    /// <summary>已退款：名額已釋放</summary>
    Refunded,
}
