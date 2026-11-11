using System.Security.Cryptography;

namespace EventHub.Web.Infrastructure;

/// <summary>
/// 產生英數字 20 碼、不重複的編號：前綴 2 碼 + yyMMddHHmmss（12 碼）+ 隨機 6 碼。
/// - 訂單編號：EH261104153012K7QX2M
/// - 綠界 MerchantTradeNo（每次付款一個）：EP261104153012K7QX2M
/// 隨機碼排除容易混淆的 0/O、1/I，方便客服口頭核對。
/// </summary>
public static class OrderNumberGenerator
{
    private const string RandomChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>訂單編號</summary>
    public static string Generate(DateTime now) => Build("EH", now);

    /// <summary>送給綠界的 MerchantTradeNo（綠界規定不可重複使用，每次前往付款都產生新的）</summary>
    public static string GenerateMerchantTradeNo(DateTime now) => Build("EP", now);

    private static string Build(string prefix, DateTime now) =>
        prefix + now.ToString("yyMMddHHmmss") + RandomNumberGenerator.GetString(RandomChars, 6);
}
