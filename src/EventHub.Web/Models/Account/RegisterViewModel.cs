using System.ComponentModel.DataAnnotations;

namespace EventHub.Web.Models.Account;

public class RegisterViewModel
{
    [Required(ErrorMessage = "請選擇身分")]
    [Display(Name = "身分")]
    public AccountType? AccountType { get; set; }

    [Required(ErrorMessage = "請輸入顯示名稱")]
    [StringLength(30, ErrorMessage = "顯示名稱最多 {1} 個字")]
    [Display(Name = "顯示名稱")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "請輸入 Email")]
    [EmailAddress(ErrorMessage = "Email 格式不正確")]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "請輸入密碼")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "密碼至少需要 {2} 個字元")]
    [DataType(DataType.Password)]
    [Display(Name = "密碼")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "請再輸入一次密碼")]
    [Compare(nameof(Password), ErrorMessage = "兩次輸入的密碼不一致")]
    [DataType(DataType.Password)]
    [Display(Name = "確認密碼")]
    public string ConfirmPassword { get; set; } = "";
}
