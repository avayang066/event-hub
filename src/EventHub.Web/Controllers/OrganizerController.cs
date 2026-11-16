using EventHub.Web.Data;
using EventHub.Web.Models.Organizer;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>主辦方專區：只有 Organizer 身分能進入。</summary>
[Authorize(Roles = AppRoles.Organizer)]
public class OrganizerController(
    OrganizerEventService events,
    ReportService reports,
    UserManager<ApplicationUser> userManager) : Controller
{
    /// <summary>我的活動 + 收款摘要</summary>
    public async Task<IActionResult> Index()
    {
        var organizerId = userManager.GetUserId(User)!;
        var payouts = await reports.GetPayoutsAsync(organizerId);

        return View(new OrganizerDashboardViewModel
        {
            Events = await events.ListAsync(organizerId),
            Payout = payouts.FirstOrDefault(),
        });
    }
}
