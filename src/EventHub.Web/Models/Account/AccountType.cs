using System.ComponentModel.DataAnnotations;

namespace EventHub.Web.Models.Account;

/// <summary>註冊時可選的身分。刻意不含 Admin，管理員只能由設定檔建立。</summary>
public enum AccountType
{
    [Display(Name = "我是報名者")]
    Attendee = 1,

    [Display(Name = "我是主辦方")]
    Organizer = 2,
}
