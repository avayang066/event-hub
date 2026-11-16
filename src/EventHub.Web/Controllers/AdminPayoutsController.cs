using EventHub.Web.Data;
using EventHub.Web.Models.Reports;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>管理員：每位主辦方的撥款彙總。</summary>
[Authorize(Roles = AppRoles.Admin)]
[Route("Admin/Payouts")]
public class AdminPayoutsController(ReportService reports) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var items = await reports.GetPayoutsAsync();
        return View(new PayoutListViewModel { Items = items });
    }
}
