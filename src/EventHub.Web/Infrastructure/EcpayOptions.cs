namespace EventHub.Web.Infrastructure;

/// <summary>綠界全方位金流（AIO）設定，對應 appsettings 的 "Ecpay" 區段。</summary>
public class EcpayOptions
{
    public const string SectionName = "Ecpay";

    public string MerchantId { get; set; } = "";
    public string HashKey { get; set; } = "";
    public string HashIV { get; set; } = "";

    /// <summary>結帳頁網址（測試：payment-stage，正式：payment）</summary>
    public string CheckoutUrl { get; set; } = "";

    /// <summary>
    /// 綠界伺服器回呼用的對外網址（ReturnURL 必須是公開網址）。
    /// 本機開發請填 ngrok 產生的 https 網址。
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>平台手續費率，例如 0.05 = 5%</summary>
    public decimal PlatformFeeRate { get; set; }
}
