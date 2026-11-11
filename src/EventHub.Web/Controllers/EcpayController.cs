using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>
/// 綠界伺服器呼叫的端點（不是給使用者瀏覽的）。
/// 綠界不會帶登入 Cookie 與防偽權杖，安全性靠 CheckMacValue 檢查碼。
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("Payments/Ecpay")]
public class EcpayController(EcpayPaymentService ecpay) : Controller
{
    /// <summary>付款結果通知（ReturnURL）。必須回傳純文字 1|OK，否則綠界會重送。</summary>
    [HttpPost("Notify")]
    public async Task<IActionResult> Notify()
    {
        if (!Request.HasFormContentType)
        {
            return Content("0|Bad Request", "text/plain");
        }

        var form = (await Request.ReadFormAsync())
            .ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal);

        return Content(await ecpay.HandleNotificationAsync(form), "text/plain");
    }
}
