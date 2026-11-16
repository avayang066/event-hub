using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>管理員：訂單列表、訂單詳情、標記退款。</summary>
[Authorize(Roles = AppRoles.Admin)]
[Route("Admin/Orders")]
public class AdminOrdersController(
    OrderQueryService orders,
    OrderStateService orderState,
    ReportService reports,
    UserManager<ApplicationUser> userManager) : Controller
{
    /// <summary>訂單列表：可依關鍵字（訂單編號、姓名、Email、手機）、狀態、報名日期篩選</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? q, OrderStatus? status, DateTime? from, DateTime? to, int page = 1)
    {
        var model = await reports.ListOrdersAsync(q, status, from, to, page);
        return View(model);
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
