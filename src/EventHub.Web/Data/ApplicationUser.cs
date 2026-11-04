using Microsoft.AspNetCore.Identity;

namespace EventHub.Web.Data;

/// <summary>
/// 平台會員。角色（主辦方／報名者）用 Identity Roles 區分，見 <see cref="AppRoles"/>。
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
