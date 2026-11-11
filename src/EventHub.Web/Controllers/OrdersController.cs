using EventHub.Web.Data;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>報名者查看自己的報名（訂單）。</summary>
[Authorize(Roles = AppRoles.Attendee)]
[Route("Orders")]
public class OrdersController(
    OrderQueryService orders,
    OrderStateService orderState,
    EcpayPaymentService ecpay,
    UserManager<ApplicationUser> userManager) : Controller
{
    private string UserId => userManager.GetUserId(User)!;

    /// <summary>我的報名</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await orders.ListAsync(UserId));

    /// <summary>訂單詳情；不是自己的訂單一律 404</summary>
    [HttpGet("{orderNo}")]
    public async Task<IActionResult> Details(string orderNo)
    {
        var model = await orders.GetAsync(orderNo, UserId);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>
    /// 前往綠界付款：產生付款表單，頁面載入後自動送出到綠界。
    /// 付款完成後，綠界頁面的「返回商店」會回到訂單詳情。
    /// </summary>
    [HttpPost("{orderNo}/Pay")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(string orderNo)
    {
        // 用目前瀏覽的網址（localhost 或 ngrok）組返回網址，回來時登入狀態才會在
        var clientBackUrl = Url.Action(nameof(Details), "Orders", new { orderNo }, Request.Scheme)!;

        var (result, form) = await ecpay.CreateCheckoutAsync(orderNo, UserId, clientBackUrl);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Message));
            return RedirectToAction(nameof(Details), new { orderNo });
        }

        // 不讓瀏覽器快取：按上一頁回來時不會用同一個 MerchantTradeNo 再送一次
        Response.Headers.CacheControl = "no-store";
        return View("Checkout", form);
    }

    /// <summary>取消自己的待付款訂單</summary>
    [HttpPost("{orderNo}/Cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string orderNo)
    {
        var result = await orderState.CancelByAttendeeAsync(orderNo, UserId);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "已取消報名，名額已釋出。";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Message));
        }

        return RedirectToAction(nameof(Details), new { orderNo });
    }
}
