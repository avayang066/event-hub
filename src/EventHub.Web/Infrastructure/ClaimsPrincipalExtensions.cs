using System.Security.Claims;
using EventHub.Web.Data;

namespace EventHub.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>顯示名稱；沒有設定就用 Email。</summary>
    public static string GetDisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimsPrincipalFactory.DisplayNameClaimType)
        ?? user.Identity?.Name
        ?? "";

    /// <summary>身分的中文名稱，給頁首顯示用。同時有多個身分時以權限較高者為準。</summary>
    public static string GetRoleLabel(this ClaimsPrincipal user) =>
        user.IsInRole(AppRoles.Admin) ? "管理員"
        : user.IsInRole(AppRoles.Organizer) ? "主辦方"
        : user.IsInRole(AppRoles.Attendee) ? "報名者"
        : "";
}
