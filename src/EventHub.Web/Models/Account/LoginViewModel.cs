using System.ComponentModel.DataAnnotations;

namespace EventHub.Web.Models.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "請輸入 Email")]
    [EmailAddress(ErrorMessage = "Email 格式不正確")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "請輸入密碼")]
    [DataType(DataType.Password)]
    [Display(Name = "密碼")]
    public string Password { get; set; } = "";

    [Display(Name = "保持登入")]
    public bool RememberMe { get; set; }
}
