using EventHub.Web.Data;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>前台活動頁面，不需登入。</summary>
[Route("Events")]
public class EventsController(
    EventQueryService events,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var model = await events.GetDetailAsync(id, userManager.GetUserId(User));
        if (model is null)
        {
            return NotFound();
        }

        return View(model);
    }
}
