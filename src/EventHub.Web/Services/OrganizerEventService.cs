using EventHub.Web.Data;
using EventHub.Web.Data.Entities;
using EventHub.Web.Infrastructure;
using EventHub.Web.Models.Organizer;
using Microsoft.EntityFrameworkCore;

namespace EventHub.Web.Services;

/// <summary>
/// 主辦方管理自己的活動：新增、編輯、上架、下架、取消。
/// 所有方法都以 organizerId 限定範圍，主辦方碰不到別人的活動。
/// </summary>
public class OrganizerEventService(
    AppDbContext db,
    EventImageStorage images,
    TimeProvider timeProvider,
    ILogger<OrganizerEventService> logger)
{
    private DateTime Now => timeProvider.GetTaiwanNow();

    // ---------------- 查詢 ----------------

    public Task<List<Event>> ListAsync(string organizerId) =>
        db.Events
            .AsNoTracking()
            .Where(e => e.OrganizerId == organizerId)
            .OrderByDescending(e => e.StartAt)
            .ToListAsync();

    /// <summary>找自己的活動；不存在或不是自己的都回傳 null。</summary>
    public Task<Event?> FindAsync(int id, string organizerId) =>
        db.Events.FirstOrDefaultAsync(e => e.Id == id && e.OrganizerId == organizerId);

    // ---------------- 新增 ----------------

    public async Task<(OperationResult Result, int EventId)> CreateAsync(string organizerId, EventFormViewModel form)
    {
        var result = new OperationResult();

        if (form.StartAt <= Now)
        {
            result.AddError(nameof(form.StartAt), "開始時間必須晚於現在");
        }

        var imageExtension = ValidateImage(form, result);

        if (!result.Succeeded)
        {
            return (result, 0);
        }

        var now = Now;
        var entity = new Event
        {
            OrganizerId = organizerId,
            Status = EventStatus.Draft,
            CreatedAt = now,
        };
        ApplyForm(entity, form, now);

        if (imageExtension is not null)
        {
            entity.CoverImagePath = await images.SaveAsync(form.CoverImage!, imageExtension);
        }

        try
        {
            db.Events.Add(entity);
            await db.SaveChangesAsync();
        }
        catch
        {
            // 資料沒存進去，剛存的圖片也要刪掉，避免留下沒人用的檔案
            images.Delete(entity.CoverImagePath);
            throw;
        }

        logger.LogInformation("主辦方 {OrganizerId} 建立活動 {EventId}", organizerId, entity.Id);
        return (result, entity.Id);
    }

    // ---------------- 編輯 ----------------

    public async Task<OperationResult> UpdateAsync(Event entity, EventFormViewModel form)
    {
        var result = new OperationResult();

        if (entity.Status == EventStatus.Cancelled)
        {
            return OperationResult.Fail("已取消的活動不能修改。");
        }

        if (form.StartAt != entity.StartAt && form.StartAt <= Now)
        {
            result.AddError(nameof(form.StartAt), "開始時間必須晚於現在");
        }

        if (form.Capacity < entity.ReservedCount)
        {
            result.AddError(nameof(form.Capacity), $"已有 {entity.ReservedCount} 人報名，名額不能少於 {entity.ReservedCount}");
        }

        if (form.Price != entity.Price && entity.ReservedCount > 0)
        {
            result.AddError(nameof(form.Price), "已有人報名，不能修改報名費");
        }

        var imageExtension = ValidateImage(form, result);

        if (!result.Succeeded)
        {
            return result;
        }

        var oldImage = entity.CoverImagePath;
        string? newImage = null;

        ApplyForm(entity, form, Now);

        if (imageExtension is not null)
        {
            newImage = await images.SaveAsync(form.CoverImage!, imageExtension);
            entity.CoverImagePath = newImage;
        }
        else if (form.RemoveCoverImage)
        {
            entity.CoverImagePath = null;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // 最可能的情況：儲存的同時有人報名，名額變得比已報名人數少（資料庫 check constraint 擋下）
            logger.LogWarning(ex, "更新活動 {EventId} 失敗", entity.Id);
            images.Delete(newImage);
            return OperationResult.Fail("儲存失敗，報名人數可能剛好有變動，請重新整理後再試。");
        }

        // 資料庫已更新成功，才刪掉被換掉的舊圖
        if (oldImage != entity.CoverImagePath)
        {
            images.Delete(oldImage);
        }

        return result;
    }

    // ---------------- 狀態變更 ----------------
    // 先用已載入的資料檢查並給出清楚的錯誤訊息，
    // 再用「條件式 UPDATE」實際更新：條件不成立（例如同一時間有人報名）就更新 0 筆，不會改錯。

    /// <summary>草稿 → 已上架</summary>
    public async Task<OperationResult> PublishAsync(Event entity)
    {
        if (entity.Status != EventStatus.Draft)
        {
            return OperationResult.Fail("只有草稿可以上架。");
        }

        var now = Now;
        if (entity.StartAt <= now)
        {
            return OperationResult.Fail("活動開始時間已過，請先修改時間再上架。");
        }

        if (entity.RegistrationDeadline is { } deadline && deadline <= now)
        {
            return OperationResult.Fail("報名截止時間已過，請先修改再上架。");
        }

        var updated = await db.Events
            .Where(e => e.Id == entity.Id && e.Status == EventStatus.Draft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.Published)
                .SetProperty(e => e.UpdatedAt, now));

        return updated == 1 ? OperationResult.Success() : OperationResult.Fail("活動狀態已變更，請重新整理後再試。");
    }

    /// <summary>已上架 → 草稿（沒有人報名時才可以）</summary>
    public async Task<OperationResult> UnpublishAsync(Event entity)
    {
        if (entity.Status != EventStatus.Published)
        {
            return OperationResult.Fail("只有已上架的活動可以下架。");
        }

        if (entity.ReservedCount > 0)
        {
            return OperationResult.Fail("已有人報名，不能下架；如需停辦請使用「取消活動」。");
        }

        var updated = await db.Events
            .Where(e => e.Id == entity.Id && e.Status == EventStatus.Published && e.ReservedCount == 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.Draft)
                .SetProperty(e => e.UpdatedAt, Now));

        return updated == 1
            ? OperationResult.Success()
            : OperationResult.Fail("剛好有人報名，無法下架；如需停辦請使用「取消活動」。");
    }

    /// <summary>草稿／已上架 → 已取消（不可復原）</summary>
    public async Task<OperationResult> CancelAsync(Event entity)
    {
        if (entity.Status == EventStatus.Cancelled)
        {
            return OperationResult.Fail("活動已經取消了。");
        }

        var updated = await db.Events
            .Where(e => e.Id == entity.Id && e.Status != EventStatus.Cancelled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, EventStatus.Cancelled)
                .SetProperty(e => e.UpdatedAt, Now));

        if (updated == 1)
        {
            logger.LogInformation("活動 {EventId} 已取消（已報名 {Count} 人）", entity.Id, entity.ReservedCount);
        }

        return updated == 1 ? OperationResult.Success() : OperationResult.Fail("活動狀態已變更，請重新整理後再試。");
    }

    // ---------------- 共用 ----------------

    private static void ApplyForm(Event entity, EventFormViewModel form, DateTime now)
    {
        entity.Title = form.Title.Trim();
        entity.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
        entity.StartAt = form.StartAt!.Value;
        entity.EndAt = form.EndAt!.Value;
        entity.RegistrationDeadline = form.RegistrationDeadline;
        entity.Location = form.Location.Trim();
        entity.Price = form.Price!.Value;
        entity.Capacity = form.Capacity!.Value;
        entity.UpdatedAt = now;
    }

    /// <summary>有上傳圖片就檢查；回傳副檔名，沒上傳或不合格回傳 null。</summary>
    private static string? ValidateImage(EventFormViewModel form, OperationResult result)
    {
        if (form.CoverImage is null)
        {
            return null;
        }

        var error = EventImageStorage.Validate(form.CoverImage, out var extension);
        if (error is not null)
        {
            result.AddError(nameof(form.CoverImage), error);
            return null;
        }

        return extension;
    }
}
