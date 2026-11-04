using System.Diagnostics;
using EventHub.Web.Models;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

public class HomeController(EventQueryService events) : Controller
{
    /// <summary>首頁 = 活動列表；q = 搜尋標題</summary>
    public async Task<IActionResult> Index(int page = 1, string? q = null)
    {
        var model = await events.ListUpcomingAsync(page, q);
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
