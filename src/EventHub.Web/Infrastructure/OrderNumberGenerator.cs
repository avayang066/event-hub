using System.Security.Cryptography;

namespace EventHub.Web.Infrastructure;

/// <summary>
/// 產生訂單編號，同時作為綠界的 MerchantTradeNo（英數字、最長 20 碼、不可重複）。
/// 格式：EH + yyMMddHHmmss（12 碼）+ 隨機 6 碼 = 20 碼，例如 EH261104153012K7QX2M。
/// 隨機碼排除容易混淆的 0/O、1/I，方便客服口頭核對。
/// </summary>
public static class OrderNumberGenerator
{
    private const string RandomChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Generate(DateTime now) =>
        "EH" + now.ToString("yyMMddHHmmss") + RandomNumberGenerator.GetString(RandomChars, 6);
}
