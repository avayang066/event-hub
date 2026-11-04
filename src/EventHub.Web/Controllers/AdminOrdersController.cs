using EventHub.Web.Data;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>管理員：查詢訂單、標記退款。完整後台列表於後續步驟實作。</summary>
[Authorize(Roles = AppRoles.Admin)]
[Route("Admin/Orders")]
public class AdminOrdersController(
    OrderQueryService orders,
    OrderStateService orderState,
    UserManager<ApplicationUser> userManager) : Controller
{
    /// <summary>以訂單編號查詢</summary>
    [HttpGet("")]
    public IActionResult Index(string? q)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            return RedirectToAction(nameof(Details), new { orderNo = q.Trim().ToUpperInvariant() });
        }

        return View();
    }

    [HttpGet("{orderNo}")]
    public async Task<IActionResult> Details(string orderNo)
    {
        var model = await orders.GetForAdminAsync(orderNo);
        if (model is null)
        {
            TempData["ErrorMessage"] = $"找不到訂單 {orderNo}。";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    /// <summary>已在綠界後台完成退款後，標記為已退款</summary>
    [HttpPost("{orderNo}/Refund")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refund(string orderNo)
    {
        var result = await orderState.MarkRefundedAsync(orderNo, userManager.GetUserId(User)!);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "已標記為已退款，名額已釋出。";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Message));
        }

        return RedirectToAction(nameof(Details), new { orderNo });
    }
}
