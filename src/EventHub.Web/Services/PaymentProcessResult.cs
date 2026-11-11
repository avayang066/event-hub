namespace EventHub.Web.Services;

/// <summary>綠界付款通知的處理結果（存在 payment_notifications.process_result）。</summary>
public static class PaymentProcessResult
{
    /// <summary>已收到、處理中（處理完會更新成下列其中一種；停在這裡代表處理到一半失敗）</summary>
    public const string Received = "Received";

    /// <summary>付款成功，訂單改為已付款</summary>
    public const string Paid = "Paid";

    /// <summary>同一筆綠界交易的重複通知，已忽略</summary>
    public const string Duplicate = "Duplicate";

    /// <summary>訂單已用另一筆綠界交易付過款，這筆是重複付款，需要退款</summary>
    public const string DuplicatePayment = "DuplicatePayment";

    /// <summary>訂單已取消（逾期或本人取消）後才付款成功，需要退款</summary>
    public const string PaidAfterCancelled = "PaidAfterCancelled";

    /// <summary>綠界回傳金額與送出金額不符，未改訂單狀態，需人工確認</summary>
    public const string AmountMismatch = "AmountMismatch";

    /// <summary>綠界後台的「模擬付款」，不是真的付款，未改訂單狀態</summary>
    public const string Simulated = "Simulated";

    /// <summary>綠界回報付款失敗（RtnCode ≠ 1）</summary>
    public const string PaymentFailed = "PaymentFailed";

    /// <summary>檢查碼或特店編號不符，可能是偽造的通知</summary>
    public const string InvalidCheckMac = "InvalidCheckMac";

    /// <summary>找不到對應的付款紀錄</summary>
    public const string OrderNotFound = "OrderNotFound";

    /// <summary>處理時發生錯誤，綠界會再重送</summary>
    public const string Error = "Error";

    /// <summary>需要管理員處理（退款或人工確認）</summary>
    public static bool NeedsAttention(string result) =>
        result is DuplicatePayment or PaidAfterCancelled or AmountMismatch or Error or Received;

    public static string ToLabel(string result) => result switch
    {
        Received => "處理中斷",
        Paid => "付款成功",
        Duplicate => "重複通知（已忽略）",
        DuplicatePayment => "重複付款（需退款）",
        PaidAfterCancelled => "訂單取消後才付款（需退款）",
        AmountMismatch => "金額不符（需確認）",
        Simulated => "模擬付款（已忽略）",
        PaymentFailed => "付款失敗",
        InvalidCheckMac => "檢查碼錯誤",
        OrderNotFound => "找不到訂單",
        Error => "處理失敗",
        _ => result,
    };

    public static string ToBadgeClass(string result) =>
        result == Paid ? "text-bg-success"
        : NeedsAttention(result) ? "text-bg-danger"
        : "text-bg-secondary";
}
