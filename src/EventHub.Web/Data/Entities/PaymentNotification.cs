namespace EventHub.Web.Data.Entities;

/// <summary>
/// 綠界付款結果通知（ReturnURL）的原始紀錄。每收到一次就存一筆，
/// 不論檢查碼是否正確、是否為重複通知，方便對帳與除錯。
/// </summary>
public class PaymentNotification
{
    public long Id { get; set; }

    /// <summary>對應的訂單；檢查碼錯誤或找不到訂單時為 null</summary>
    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>綠界回傳的 MerchantTradeNo（= orders.order_no）</summary>
    public string MerchantTradeNo { get; set; } = "";

    /// <summary>綠界交易編號 TradeNo</summary>
    public string? EcpayTradeNo { get; set; }

    /// <summary>RtnCode：1 = 付款成功</summary>
    public int RtnCode { get; set; }
    public string? RtnMsg { get; set; }

    /// <summary>TradeAmt：綠界回傳的交易金額，需與訂單金額比對</summary>
    public int TradeAmount { get; set; }

    public string? PaymentType { get; set; }

    /// <summary>綠界回傳的付款時間（字串原樣保存）</summary>
    public string? PaymentDate { get; set; }

    /// <summary>SimulatePaid：1 = 綠界後台的「模擬付款」，不是真的付款</summary>
    public bool IsSimulated { get; set; }

    /// <summary>CheckMacValue 是否驗證通過</summary>
    public bool IsCheckMacValid { get; set; }

    /// <summary>處理結果，例如 Paid、Duplicate、InvalidCheckMac、AmountMismatch、OrderNotFound</summary>
    public string ProcessResult { get; set; } = "";

    /// <summary>完整的 POST 內容（原樣保存）</summary>
    public string RawPayload { get; set; } = "";

    public DateTime ReceivedAt { get; set; }
}
