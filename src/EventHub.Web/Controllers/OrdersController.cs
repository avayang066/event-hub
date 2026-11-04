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
}
