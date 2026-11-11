namespace EventHub.Web.Data.Entities;

/// <summary>
/// 一次「前往綠界付款」。綠界規定 MerchantTradeNo 送出後就不能再用，
/// 報名者關掉付款頁再按一次付款時，必須換一個新的編號，所以一張訂單可能有多筆付款嘗試。
/// 綠界付款通知帶回 MerchantTradeNo，透過這張表找回是哪一張訂單。
/// </summary>
public class PaymentAttempt
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>送給綠界的特店訂單編號（英數字、最長 20 碼、全站唯一），例如 EP261111153012K7QX2M</summary>
    public string MerchantTradeNo { get; set; } = "";

    /// <summary>送給綠界的金額（TotalAmount），付款通知的 TradeAmt 要和它一致</summary>
    public int Amount { get; set; }

    public DateTime CreatedAt { get; set; }
}
