using EventHub.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

/// <summary>主辦方專區：只有 Organizer 身分能進入。</summary>
[Authorize(Roles = AppRoles.Organizer)]
public class OrganizerController : Controller
{
    /// <summary>我的活動（第 2 步實作活動管理）</summary>
    public IActionResult Index() => View();
}
