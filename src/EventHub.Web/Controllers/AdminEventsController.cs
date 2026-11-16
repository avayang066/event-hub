using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Reports;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>管理員：活動總覽（每場活動的報名數與金額）、任一活動的報名名單。</summary>
[Authorize(Roles = AppRoles.Admin)]
[Route("Admin/Events")]
public class AdminEventsController(ReportService reports, TimeProvider timeProvider) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, EventStatus? status, string? organizer, int page = 1)
    {
        var model = await reports.ListEventsAsync(q, status, organizer, page);
        return View(model);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Registrations(int id, RegistrationFilter filter = RegistrationFilter.Active)
    {
        var model = await reports.GetEventRegistrationsAsync(id, organizerId: null, filter, isAdminView: true);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet("{id:int}/Export")]
    public async Task<IActionResult> ExportRegistrations(int id, RegistrationFilter filter = RegistrationFilter.Active)
    {
        var model = await reports.GetEventRegistrationsAsync(id, organizerId: null, filter, isAdminView: true);
        if (model is null)
        {
            return NotFound();
        }

        return File(RegistrationCsv.Build(model), "text/csv; charset=utf-8",
            RegistrationCsv.FileName(model, timeProvider.GetTaiwanNow()));
    }
}
