namespace EventHub.Web.Data.Entities;

/// <summary>
/// 報名訂單：一筆訂單 = 一位報名者佔一個名額。
/// 時間欄位一律存台灣時間（UTC+8）。
/// </summary>
public class Order
{
    public int Id { get; set; }

    /// <summary>
    /// 平台訂單編號（英數字 20 碼、全站唯一），例如 EH261104153012K7QX2M。
    /// 送給綠界的 MerchantTradeNo 每次付款都不同，記在 payment_attempts；
    /// 這個編號放在綠界的 CustomField1，方便在綠界後台對照。
    /// </summary>
    public string OrderNo { get; set; } = "";

    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    /// <summary>報名者帳號（users.Id）</summary>
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// 主辦方（users.Id）。從活動複製一份，方便依主辦方查詢報名與結算，
    /// 不必每次都 JOIN events。
    /// </summary>
    public string OrganizerId { get; set; } = "";
    public ApplicationUser Organizer { get; set; } = null!;

    // ---- 報名表單填寫的資料（報名當下的快照，會員之後改資料不影響）----
    public string AttendeeName { get; set; } = "";
    public string AttendeePhone { get; set; } = "";
    public string AttendeeEmail { get; set; } = "";
    public string? Note { get; set; }

    // ---- 金額（新台幣整數元，報名當下的快照，活動之後改價不影響）----

    /// <summary>報名者實付金額 = 活動報名費</summary>
    public int Amount { get; set; }

    /// <summary>平台手續費 = Amount × 手續費率（四捨五入）</summary>
    public int PlatformFee { get; set; }

    /// <summary>應撥給主辦方 = Amount − PlatformFee</summary>
    public int OrganizerAmount { get; set; }

    // ---- 狀態 ----
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    /// <summary>待付款保留名額的期限，超過就取消訂單、釋放名額</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? RefundedAt { get; set; }

    /// <summary>退款金額（目前只支援全額退款，保留欄位給部分退款）</summary>
    public int? RefundAmount { get; set; }

    // ---- 綠界付款結果 ----

    /// <summary>綠界交易編號 TradeNo（付款成功後由通知帶回）</summary>
    public string? EcpayTradeNo { get; set; }

    /// <summary>付款方式，例如 Credit_CreditCard、ATM_TAISHIN</summary>
    public string? PaymentType { get; set; }

    // ---- 通知信 ----

    /// <summary>報名成功信寄出時間；有值就不重寄（避免重複通知時重複寄信）</summary>
    public DateTime? ConfirmationEmailSentAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
