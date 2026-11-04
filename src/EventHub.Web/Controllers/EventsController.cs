using EventHub.Web.Data;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Registration;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>前台活動頁面。瀏覽不需登入；報名限報名者身分。</summary>
[Route("Events")]
public class EventsController(
    EventQueryService events,
    RegistrationService registration,
    TimeProvider timeProvider,
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

    // ---------------- 報名 ----------------

    [HttpGet("{id:int}/Register")]
    [Authorize(Roles = AppRoles.Attendee)]
    public async Task<IActionResult> Register(int id)
    {
        var e = await registration.FindEventAsync(id);
        if (e is null)
        {
            return NotFound();
        }

        var userId = userManager.GetUserId(User)!;
        if (await registration.FindActiveOrderNoAsync(id, userId) is { } existingOrderNo)
        {
            TempData["StatusMessage"] = "你已經報名過這個活動了。";
            return RedirectToAction("Details", "Orders", new { orderNo = existingOrderNo });
        }

        var unavailable = RegistrationService.GetUnavailableMessage(
            EventAvailability.Evaluate(e, timeProvider.GetTaiwanNow()));
        if (unavailable is not null)
        {
            TempData["ErrorMessage"] = unavailable;
            return RedirectToAction(nameof(Details), new { id });
        }

        // 預先帶入會員資料
        var user = await userManager.GetUserAsync(User);
        var form = new RegistrationFormViewModel
        {
            AttendeeName = user?.DisplayName ?? "",
            AttendeeEmail = user?.Email ?? "",
            AttendeePhone = user?.PhoneNumber ?? "",
        };
        form.LoadEvent(e);
        return View(form);
    }

    [HttpPost("{id:int}/Register")]
    [Authorize(Roles = AppRoles.Attendee)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(int id, RegistrationFormViewModel form)
    {
        var e = await registration.FindEventAsync(id);
        if (e is null)
        {
            return NotFound();
        }

        form.LoadEvent(e);

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var result = await registration.RegisterAsync(id, userManager.GetUserId(User)!, form);

        if (result.AlreadyRegistered)
        {
            TempData["StatusMessage"] = "你已經報名過這個活動了。";
            return RedirectToAction("Details", "Orders", new { orderNo = result.OrderNo });
        }

        if (!result.Result.Succeeded)
        {
            foreach (var (field, message) in result.Result.Errors)
            {
                ModelState.AddModelError(field, message);
            }

            return View(form);
        }

        TempData["StatusMessage"] = e.Price == 0
            ? "報名成功！"
            : $"已為你保留名額，請在 {RegistrationService.PaymentHoldTime.TotalMinutes:0} 分鐘內完成付款。";
        return RedirectToAction("Details", "Orders", new { orderNo = result.OrderNo });
    }
}
