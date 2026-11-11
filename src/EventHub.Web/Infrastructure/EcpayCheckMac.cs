using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace EventHub.Web.Infrastructure;

/// <summary>
/// 綠界檢查碼 CheckMacValue（SHA256，EncryptType=1）。
/// 官方說明：https://developers.ecpay.com.tw/2902/
///
///   1. 除了 CheckMacValue 以外的參數，依參數名稱 A→Z 排序（不分大小寫），以 &amp; 串起來
///   2. 前面加上 HashKey=...&amp;，後面加上 &amp;HashIV=...
///   3. URL encode（綠界以 .NET 的 HttpUtility.UrlEncode 為準：空白變 +，- _ . ! * ( ) 不編碼）
///   4. 轉小寫
///   5. SHA256 後轉成大寫十六進位
/// </summary>
public static class EcpayCheckMac
{
    public const string FieldName = "CheckMacValue";

    public static string Compute(IEnumerable<KeyValuePair<string, string>> parameters, string hashKey, string hashIV)
    {
        var sorted = parameters
            .Where(p => !string.Equals(p.Key, FieldName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key}={p.Value}");

        var raw = $"HashKey={hashKey}&{string.Join('&', sorted)}&HashIV={hashIV}";
        var encoded = HttpUtility.UrlEncode(raw).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(encoded));

        return Convert.ToHexString(hash);
    }

    /// <summary>驗證綠界送來的參數（含 CheckMacValue）是否正確、未被竄改。</summary>
    public static bool Verify(IReadOnlyDictionary<string, string> parameters, string hashKey, string hashIV)
    {
        if (!parameters.TryGetValue(FieldName, out var received) || string.IsNullOrEmpty(received))
        {
            return false;
        }

        var expected = Compute(parameters, hashKey, hashIV);

        // 固定時間比對，避免從回應時間猜出正確的檢查碼
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(received.ToUpperInvariant()));
    }
}
