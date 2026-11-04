using System.ComponentModel.DataAnnotations;
using EventHub.Web.Data.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EventHub.Web.Models.Organizer;

/// <summary>主辦方新增／編輯活動的表單。</summary>
public class EventFormViewModel : IValidatableObject
{
    // ---------------- 使用者填寫的欄位 ----------------

    [Required(ErrorMessage = "請輸入活動標題")]
    [StringLength(100, ErrorMessage = "標題最多 {1} 個字")]
    [Display(Name = "活動標題")]
    public string Title { get; set; } = "";

    [StringLength(5000, ErrorMessage = "活動說明最多 {1} 個字")]
    [Display(Name = "活動說明")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "請選擇開始時間")]
    [Display(Name = "開始時間")]
    public DateTime? StartAt { get; set; }

    [Required(ErrorMessage = "請選擇結束時間")]
    [Display(Name = "結束時間")]
    public DateTime? EndAt { get; set; }

    [Display(Name = "報名截止時間")]
    public DateTime? RegistrationDeadline { get; set; }

    [Required(ErrorMessage = "請輸入地點")]
    [StringLength(200, ErrorMessage = "地點最多 {1} 個字")]
    [Display(Name = "地點")]
    public string Location { get; set; } = "";

    [Required(ErrorMessage = "請輸入報名費")]
    [Range(0, 999_999, ErrorMessage = "報名費需介於 {1} 到 {2} 元")]
    [Display(Name = "報名費（元）")]
    public int? Price { get; set; }

    [Required(ErrorMessage = "請輸入名額")]
    [Range(1, 100_000, ErrorMessage = "名額需介於 {1} 到 {2} 人")]
    [Display(Name = "名額")]
    public int? Capacity { get; set; }

    [Display(Name = "封面圖片")]
    public IFormFile? CoverImage { get; set; }

    [Display(Name = "移除目前的封面圖片")]
    public bool RemoveCoverImage { get; set; }

    // ---------------- 只用來顯示，由伺服器從資料庫填入，不接受表單送來的值 ----------------

    [BindNever] public int? Id { get; set; }
    [BindNever] public EventStatus Status { get; set; } = EventStatus.Draft;
    [BindNever] public int ReservedCount { get; set; }
    [BindNever] public string? CurrentCoverImagePath { get; set; }

    public bool IsNew => Id is null;
    public bool HasRegistrations => ReservedCount > 0;

    public static EventFormViewModel FromEntity(Event e)
    {
        var model = new EventFormViewModel
        {
            Title = e.Title,
            Description = e.Description,
            StartAt = e.StartAt,
            EndAt = e.EndAt,
            RegistrationDeadline = e.RegistrationDeadline,
            Location = e.Location,
            Price = e.Price,
            Capacity = e.Capacity,
        };
        model.LoadDisplayFields(e);
        return model;
    }

    /// <summary>重新從資料庫填入顯示用欄位（POST 回來時使用）。</summary>
    public void LoadDisplayFields(Event e)
    {
        Id = e.Id;
        Status = e.Status;
        ReservedCount = e.ReservedCount;
        CurrentCoverImagePath = e.CoverImagePath;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartAt is { } start && EndAt is { } end && end <= start)
        {
            yield return new ValidationResult("結束時間必須晚於開始時間", [nameof(EndAt)]);
        }

        if (StartAt is { } s && RegistrationDeadline is { } deadline && deadline > s)
        {
            yield return new ValidationResult("報名截止時間不能晚於開始時間", [nameof(RegistrationDeadline)]);
        }
    }
}
