namespace EventHub.Web.Models.Payments;

/// <summary>要在瀏覽器自動送出到綠界的表單：送出網址與所有隱藏欄位（含 CheckMacValue）。</summary>
public record EcpayCheckoutForm(string ActionUrl, IReadOnlyDictionary<string, string> Fields);
