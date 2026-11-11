using EventHub.Web.Infrastructure;

namespace EventHub.Tests;

/// <summary>綠界檢查碼：用官方文件的範例驗證演算法。</summary>
public class EcpayCheckMacTests
{
    /// <summary>官方範例 https://developers.ecpay.com.tw/2902/</summary>
    private static Dictionary<string, string> OfficialExample() => new()
    {
        ["TradeDesc"] = "促銷方案",
        ["PaymentType"] = "aio",
        ["MerchantTradeDate"] = "2023/03/12 15:30:23",
        ["MerchantTradeNo"] = "ecpay20230312153023",
        ["MerchantID"] = "3002607",
        ["ReturnURL"] = "https://www.ecpay.com.tw/receive.php",
        ["ItemName"] = "Apple iphone 15",
        ["TotalAmount"] = "30000",
        ["ChoosePayment"] = "ALL",
        ["EncryptType"] = "1",
    };

    private const string OfficialCheckMacValue = "6C51C9E6888DE861FD62FB1DD17029FC742634498FD813DC43D4243B5685B840";

    [Fact]
    public void Compute_matches_official_example()
    {
        var mac = EcpayCheckMac.Compute(OfficialExample(), TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV);

        Assert.Equal(OfficialCheckMacValue, mac);
    }

    [Fact]
    public void Compute_ignores_existing_CheckMacValue_field()
    {
        var fields = OfficialExample();
        fields["CheckMacValue"] = "ANYTHING";

        Assert.Equal(OfficialCheckMacValue,
            EcpayCheckMac.Compute(fields, TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV));
    }

    [Fact]
    public void Verify_accepts_correct_value_in_any_case()
    {
        var fields = OfficialExample();
        fields["CheckMacValue"] = OfficialCheckMacValue.ToLowerInvariant();

        Assert.True(EcpayCheckMac.Verify(fields, TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV));
    }

    [Fact]
    public void Verify_rejects_tampered_amount()
    {
        var fields = OfficialExample();
        fields["CheckMacValue"] = OfficialCheckMacValue;
        fields["TotalAmount"] = "1";

        Assert.False(EcpayCheckMac.Verify(fields, TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV));
    }

    [Fact]
    public void Verify_rejects_missing_value()
    {
        Assert.False(EcpayCheckMac.Verify(OfficialExample(), TestWebAppFactory.EcpayHashKey, TestWebAppFactory.EcpayHashIV));
    }

    [Fact]
    public void Special_characters_are_encoded_like_dotnet()
    {
        // 綠界以 .NET 編碼為準：- _ . ! * ( ) 不編碼、空白變 +；只要計算與驗證一致即可往返
        var fields = new Dictionary<string, string> { ["ItemName"] = "早鳥票 (限量)! *A-B_C.D ~#" };
        fields["CheckMacValue"] = EcpayCheckMac.Compute(fields, "k", "v");

        Assert.True(EcpayCheckMac.Verify(fields, "k", "v"));
    }
}
