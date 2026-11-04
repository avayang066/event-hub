using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Models.Organizer;
using EventHub.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>主辦方新增、編輯活動與變更活動狀態。</summary>
[Authorize(Roles = AppRoles.Organizer)]
[Route("Organizer/Events")]
public class OrganizerEventsController(
    OrganizerEventService events,
    UserManager<ApplicationUser> userManager) : Controller
{
    /// <summary>整個表單（含圖片）的上限；圖片本身 5MB 的限制另外檢查，才能顯示友善的錯誤訊息。</summary>
    private const long MaxRequestBytes = 10 * 1024 * 1024;

    private string UserId => userManager.GetUserId(User)!;

    // ---------------- 新增 ----------------

    [HttpGet("Create")]
    public IActionResult Create() => View(new EventFormViewModel());

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Create(EventFormViewModel form)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var (result, eventId) = await events.CreateAsync(UserId, form);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(form);
        }

        TempData["StatusMessage"] = "活動已建立（草稿）。確認內容無誤後，按「上架」就會開放報名。";
        return RedirectToAction(nameof(Edit), new { id = eventId });
    }

    // ---------------- 編輯 ----------------

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var entity = await events.FindAsync(id, UserId);
        if (entity is null)
        {
            return NotFound();
        }

        return View(EventFormViewModel.FromEntity(entity));
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Edit(int id, EventFormViewModel form)
    {
        var entity = await events.FindAsync(id, UserId);
        if (entity is null)
        {
            return NotFound();
        }

        // 顯示用欄位一律以資料庫為準
        form.LoadDisplayFields(entity);

        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var result = await events.UpdateAsync(entity, form);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View(form);
        }

        TempData["StatusMessage"] = "已儲存。";
        return RedirectToAction(nameof(Edit), new { id });
    }

    // ---------------- 狀態變更 ----------------

    [HttpPost("{id:int}/Publish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Publish(int id) =>
        ChangeStatus(id, events.PublishAsync, "活動已上架，開放報名。");

    [HttpPost("{id:int}/Unpublish")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Unpublish(int id) =>
        ChangeStatus(id, events.UnpublishAsync, "活動已下架，回到草稿。");

    [HttpPost("{id:int}/Cancel")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id) =>
        ChangeStatus(id, events.CancelAsync, "活動已取消。");

    // ---------------- 共用 ----------------

    private async Task<IActionResult> ChangeStatus(
        int id, Func<Event, Task<OperationResult>> action, string successMessage)
    {
        var entity = await events.FindAsync(id, UserId);
        if (entity is null)
        {
            return NotFound();
        }

        var result = await action(entity);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = successMessage;
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Message));
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    private void AddErrors(OperationResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }
    }
}
