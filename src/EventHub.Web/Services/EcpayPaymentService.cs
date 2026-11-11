using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventHub.Web.Services;

/// <summary>
/// 綠界全方位金流（AIO）：
/// 1. 結帳：產生一次付款嘗試（新的 MerchantTradeNo）與送往綠界的表單
/// 2. 付款結果通知（ReturnURL）：驗證檢查碼 → 找訂單 → 改為已付款，重複通知只處理一次
///
/// 官方文件：產生訂單 https://developers.ecpay.com.tw/2862/
///           付款結果通知 https://developers.ecpay.com.tw/2878/
/// </summary>
public class EcpayPaymentService(
    AppDbContext db,
    OrderStateService orderState,
    IOptions<EcpayOptions> options,
    TimeProvider timeProvider,
    ILogger<EcpayPaymentService> logger)
{
    /// <summary>綠界付款結果通知的網址路徑（ReturnURL = PublicBaseUrl + 這個路徑）</summary>
    public const string NotifyPath = "/Payments/Ecpay/Notify";

    /// <summary>回給綠界「已收到」；沒收到這個字串綠界會重送通知</summary>
    public const string AckOk = "1|OK";

    private static readonly JsonSerializerOptions RawPayloadJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    private EcpayOptions Options => options.Value;

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Options.MerchantId)
        && !string.IsNullOrWhiteSpace(Options.HashKey)
        && !string.IsNullOrWhiteSpace(Options.HashIV)
        && !string.IsNullOrWhiteSpace(Options.CheckoutUrl)
        && !string.IsNullOrWhiteSpace(Options.PublicBaseUrl);

    // ================= 結帳 =================

    /// <summary>
    /// 報名者按「前往付款」：檢查訂單可付款 → 建立付款嘗試 → 產生送往綠界的表單。
    /// </summary>
    /// <param name="clientBackUrl">綠界付款完成頁「返回商店」按鈕要回到的網址</param>
    public async Task<(OperationResult Result, EcpayCheckoutForm? Form)> CreateCheckoutAsync(
        string orderNo, string userId, string clientBackUrl)
    {
        if (!IsConfigured)
        {
            logger.LogError("綠界設定不完整（Ecpay 區段），無法結帳");
            return (OperationResult.Fail("付款功能暫時無法使用，請稍後再試。"), null);
        }

        var now = timeProvider.GetTaiwanNow();

        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.OrderNo == orderNo && o.UserId == userId)
            .Select(o => new { o.Id, o.OrderNo, o.Status, o.ExpiresAt, o.Amount, EventTitle = o.Event.Title })
            .FirstOrDefaultAsync();

        if (order is null)
        {
            return (OperationResult.Fail("找不到這筆報名。"), null);
        }

        if (order.Status != OrderStatus.Pending)
        {
            return (OperationResult.Fail(order.Status == OrderStatus.Paid
                ? "這筆報名已經付款完成。"
                : "這筆報名已取消，無法付款。"), null);
        }

        if (order.ExpiresAt is { } deadline && deadline <= now)
        {
            return (OperationResult.Fail("已超過付款期限，請回活動頁重新報名。"), null);
        }

        if (order.Amount <= 0)
        {
            return (OperationResult.Fail("這筆報名不需要付款。"), null);
        }

        // 綠界規定 MerchantTradeNo 不可重複，每次前往付款都產生新的
        var attempt = new PaymentAttempt
        {
            OrderId = order.Id,
            MerchantTradeNo = OrderNumberGenerator.GenerateMerchantTradeNo(now),
            Amount = order.Amount,
            CreatedAt = now,
        };
        db.PaymentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var fields = new Dictionary<string, string>
        {
            ["MerchantID"] = Options.MerchantId,
            ["MerchantTradeNo"] = attempt.MerchantTradeNo,
            ["MerchantTradeDate"] = now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["PaymentType"] = "aio",
            ["TotalAmount"] = order.Amount.ToString(CultureInfo.InvariantCulture),
            ["TradeDesc"] = "EventHub活動報名",
            ["ItemName"] = ToItemName(order.EventTitle),
            ["ReturnURL"] = Options.PublicBaseUrl.TrimEnd('/') + NotifyPath,
            // 只開放信用卡：ATM、超商代碼要好幾天後才付款，和 15 分鐘的名額保留時間衝突
            ["ChoosePayment"] = "Credit",
            ["EncryptType"] = "1",
            ["ClientBackURL"] = clientBackUrl,
            ["NeedExtraPaidInfo"] = "N",
            // 平台訂單編號，方便在綠界後台對照
            ["CustomField1"] = order.OrderNo,
        };
        fields[EcpayCheckMac.FieldName] = EcpayCheckMac.Compute(fields, Options.HashKey, Options.HashIV);

        logger.LogInformation("訂單 {OrderNo} 前往綠界付款 MerchantTradeNo {MerchantTradeNo} 金額 {Amount}",
            order.OrderNo, attempt.MerchantTradeNo, order.Amount);

        return (OperationResult.Success(), new EcpayCheckoutForm(Options.CheckoutUrl, fields));
    }

    /// <summary>綠界商品名稱：# 是多項商品的分隔符號要換掉；長度上限 400，超過會被截斷導致檢查碼錯誤。</summary>
    private static string ToItemName(string eventTitle)
    {
        var name = $"{eventTitle} 報名費".Replace('#', '＃');
        return name.Length <= 200 ? name : name[..200];
    }

    // ================= 付款結果通知 =================

    /// <summary>
    /// 處理綠界付款結果通知（Server POST 到 ReturnURL），回傳要回應給綠界的字串。
    /// 每一則通知都先存進 payment_notifications，再處理，處理結果寫回同一筆紀錄。
    /// </summary>
    public async Task<string> HandleNotificationAsync(IReadOnlyDictionary<string, string> form)
    {
        var notification = new PaymentNotification
        {
            MerchantTradeNo = Truncate(Get(form, "MerchantTradeNo"), 20) ?? "",
            EcpayTradeNo = Truncate(Get(form, "TradeNo"), 20),
            RtnCode = int.TryParse(Get(form, "RtnCode"), out var rtnCode) ? rtnCode : 0,
            RtnMsg = Truncate(Get(form, "RtnMsg"), 200),
            TradeAmount = int.TryParse(Get(form, "TradeAmt"), out var tradeAmount) ? tradeAmount : 0,
            PaymentType = Truncate(Get(form, "PaymentType"), 30),
            PaymentDate = Truncate(Get(form, "PaymentDate"), 30),
            IsSimulated = Get(form, "SimulatePaid") == "1",
            RawPayload = JsonSerializer.Serialize(form, RawPayloadJson),
            ProcessResult = PaymentProcessResult.Received,
            ReceivedAt = timeProvider.GetTaiwanNow(),
        };

        try
        {
            // 1. 檢查碼與特店編號：不正確就可能是偽造的，不能相信任何內容
            notification.IsCheckMacValid =
                Get(form, "MerchantID") == Options.MerchantId
                && EcpayCheckMac.Verify(form, Options.HashKey, Options.HashIV);

            db.PaymentNotifications.Add(notification);
            await db.SaveChangesAsync();

            if (!notification.IsCheckMacValid)
            {
                logger.LogWarning("綠界通知檢查碼錯誤 MerchantTradeNo {MerchantTradeNo}", notification.MerchantTradeNo);
                await SaveResultAsync(notification, PaymentProcessResult.InvalidCheckMac);
                return "0|CheckMacValue Error";
            }

            await SaveResultAsync(notification, await ProcessAsync(notification));
            return AckOk;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "處理綠界通知失敗 MerchantTradeNo {MerchantTradeNo}", notification.MerchantTradeNo);
            try
            {
                if (notification.Id != 0)
                {
                    await SaveResultAsync(notification, PaymentProcessResult.Error);
                }
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "記錄綠界通知處理失敗時又發生錯誤");
            }

            // 不回 1|OK，綠界會再重送
            return "0|Error";
        }
    }

    /// <summary>檢查碼正確之後的處理，回傳處理結果。</summary>
    private async Task<string> ProcessAsync(PaymentNotification n)
    {
        var attempt = await db.PaymentAttempts
            .AsNoTracking()
            .Where(a => a.MerchantTradeNo == n.MerchantTradeNo)
            .Select(a => new { a.OrderId, a.Amount, a.Order.OrderNo })
            .FirstOrDefaultAsync();

        if (attempt is null)
        {
            logger.LogWarning("綠界通知找不到付款紀錄 MerchantTradeNo {MerchantTradeNo}", n.MerchantTradeNo);
            return PaymentProcessResult.OrderNotFound;
        }

        n.OrderId = attempt.OrderId;

        // 2. 綠界後台的「模擬付款」不是真的收到錢，只記錄不出貨
        if (n.IsSimulated)
        {
            logger.LogInformation("訂單 {OrderNo} 收到模擬付款通知，忽略", attempt.OrderNo);
            return PaymentProcessResult.Simulated;
        }

        // 3. 付款失敗
        if (n.RtnCode != 1)
        {
            logger.LogInformation("訂單 {OrderNo} 付款失敗 RtnCode {RtnCode} {RtnMsg}", attempt.OrderNo, n.RtnCode, n.RtnMsg);
            return PaymentProcessResult.PaymentFailed;
        }

        // 4. 金額要和送出去的一致
        if (n.TradeAmount != attempt.Amount)
        {
            logger.LogError("訂單 {OrderNo} 金額不符：送出 {Expected}，綠界回傳 {Actual}",
                attempt.OrderNo, attempt.Amount, n.TradeAmount);
            return PaymentProcessResult.AmountMismatch;
        }

        // 5. 改為已付款（條件式 UPDATE：同時來的重複通知只會有一個成功）
        var outcome = await orderState.MarkPaidAsync(attempt.OrderNo, n.EcpayTradeNo, n.PaymentType);
        if (outcome == OrderStateService.MarkPaidOutcome.Paid)
        {
            // TODO 第 7 步：寄出報名成功通知信
            return PaymentProcessResult.Paid;
        }

        // 沒有改成功：判斷是重複通知，還是真的多收了一筆錢
        var order = await db.Orders
            .AsNoTracking()
            .Where(o => o.Id == attempt.OrderId)
            .Select(o => new { o.Status, o.EcpayTradeNo })
            .FirstAsync();

        if (order.EcpayTradeNo == n.EcpayTradeNo)
        {
            logger.LogInformation("訂單 {OrderNo} 重複通知 TradeNo {TradeNo}，忽略", attempt.OrderNo, n.EcpayTradeNo);
            return PaymentProcessResult.Duplicate;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            logger.LogWarning("訂單 {OrderNo} 已取消後才付款成功 TradeNo {TradeNo}，需要退款",
                attempt.OrderNo, n.EcpayTradeNo);
            return PaymentProcessResult.PaidAfterCancelled;
        }

        logger.LogWarning("訂單 {OrderNo} 重複付款：已付款交易 {PaidTradeNo}，這次 {TradeNo}，需要退款",
            attempt.OrderNo, order.EcpayTradeNo, n.EcpayTradeNo);
        return PaymentProcessResult.DuplicatePayment;
    }

    private async Task SaveResultAsync(PaymentNotification n, string result)
    {
        n.ProcessResult = result;
        await db.SaveChangesAsync();
    }

    private static string? Get(IReadOnlyDictionary<string, string> form, string key) =>
        form.TryGetValue(key, out var value) ? value : null;

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
