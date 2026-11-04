using EventHub.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EventHub.Web.Data;

public static class DbSeeder
{
    /// <summary>網站啟動時執行，可重複執行：建立角色、建立管理員帳號。</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        await SeedRolesAsync(sp.GetRequiredService<RoleManager<IdentityRole>>());
        await SeedAdminAsync(
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<IOptions<AdminAccountOptions>>().Value,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder)));
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>
    /// 設定檔有填管理員 Email 時，確保該帳號存在且具 Admin 身分。
    /// 已存在的帳號不會被改密碼。
    /// </summary>
    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager, AdminAccountOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Email))
        {
            return;
        }

        var admin = await userManager.FindByEmailAsync(options.Email);
        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(options.Password))
            {
                logger.LogWarning("已設定 AdminAccount:Email 但未設定 Password，略過建立管理員帳號");
                return;
            }

            admin = new ApplicationUser
            {
                UserName = options.Email,
                Email = options.Email,
                EmailConfirmed = true,
                DisplayName = options.DisplayName,
            };

            var created = await userManager.CreateAsync(admin, options.Password);
            if (!created.Succeeded)
            {
                logger.LogError("建立管理員帳號失敗：{Errors}",
                    string.Join("；", created.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("已建立管理員帳號 {Email}", options.Email);
        }

        if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
        {
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
        }
    }
}
