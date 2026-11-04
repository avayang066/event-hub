using System.ComponentModel.DataAnnotations;
using EventHub.Web.Data.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EventHub.Web.Models.Registration;

/// <summary>報名表單。</summary>
public class RegistrationFormViewModel
{
    // ---------------- 使用者填寫的欄位 ----------------

    [Required(ErrorMessage = "請輸入姓名")]
    [StringLength(50, ErrorMessage = "姓名最多 {1} 個字")]
    [Display(Name = "姓名")]
    public string AttendeeName { get; set; } = "";

    [Required(ErrorMessage = "請輸入手機號碼")]
    [RegularExpression(@"^09\d{2}-?\d{3}-?\d{3}$", ErrorMessage = "請輸入手機號碼，例如 0912345678")]
    [Display(Name = "手機")]
    public string AttendeePhone { get; set; } = "";

    [Required(ErrorMessage = "請輸入 Email")]
    [EmailAddress(ErrorMessage = "Email 格式不正確")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string AttendeeEmail { get; set; } = "";

    [StringLength(500, ErrorMessage = "備註最多 {1} 個字")]
    [Display(Name = "備註")]
    public string? Note { get; set; }

    // ---------------- 只用來顯示活動摘要，由伺服器從資料庫填入 ----------------

    [BindNever] public int EventId { get; set; }
    [BindNever] public string EventTitle { get; set; } = "";
    [BindNever] public DateTime StartAt { get; set; }
    [BindNever] public DateTime EndAt { get; set; }
    [BindNever] public string Location { get; set; } = "";
    [BindNever] public int Price { get; set; }

    public void LoadEvent(Event e)
    {
        EventId = e.Id;
        EventTitle = e.Title;
        StartAt = e.StartAt;
        EndAt = e.EndAt;
        Location = e.Location;
        Price = e.Price;
    }
}
