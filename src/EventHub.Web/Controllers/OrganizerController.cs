using EventHub.Web.Data;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>主辦方專區：只有 Organizer 身分能進入。</summary>
[Authorize(Roles = AppRoles.Organizer)]
public class OrganizerController(
    OrganizerEventService events,
    UserManager<ApplicationUser> userManager) : Controller
{
    /// <summary>我的活動</summary>
    public async Task<IActionResult> Index()
    {
        var list = await events.ListAsync(userManager.GetUserId(User)!);
        return View(list);
    }
}
