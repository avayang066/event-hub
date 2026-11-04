using Microsoft.AspNetCore.Identity;

namespace EventHub.Web.Data;

public static class DbSeeder
{
    /// <summary>確保三種角色存在。啟動時執行，可重複執行。</summary>
    public static async Task SeedRolesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }
}
