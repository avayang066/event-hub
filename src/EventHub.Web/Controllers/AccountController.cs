using EventHub.Web.Data;
using EventHub.Web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventHub.Web.Controllers;

public class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<AccountController> logger) : Controller
{
    // ---------------- 註冊 ----------------

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        // 只允許報名者／主辦方；任何其他值（包含竄改成 Admin 或數字）一律拒絕
        var role = model.AccountType switch
        {
            AccountType.Attendee => AppRoles.Attendee,
            AccountType.Organizer => AppRoles.Organizer,
            _ => null,
        };
        if (role is null)
        {
            ModelState.Remove(nameof(model.AccountType));
            ModelState.AddModelError(nameof(model.AccountType), "請選擇身分");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            DisplayName = model.DisplayName.Trim(),
        };

        var created = await userManager.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            AddErrors(created);
            return View(model);
        }

        var roleAdded = await userManager.AddToRoleAsync(user, role!);
        if (!roleAdded.Succeeded)
        {
            // 角色加不上就把帳號刪掉，避免留下沒有身分的帳號
            logger.LogError("註冊時設定角色失敗 {Email}：{Errors}", user.Email,
                string.Join("；", roleAdded.Errors.Select(e => e.Description)));
            await userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "註冊失敗，請稍後再試。");
            return View(model);
        }

        logger.LogInformation("新會員註冊 {Email}（{Role}）", user.Email, role);

        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToLocal(returnUrl);
    }

    // ---------------- 登入 ----------------

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // 帳號（UserName）就是 Email
        var result = await signInManager.PasswordSignInAsync(
            model.Email.Trim(), model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectToLocal(returnUrl);
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("帳號因登入失敗次數過多被鎖定 {Email}", model.Email);
            ModelState.AddModelError(string.Empty, "登入失敗次數過多，帳號已暫時鎖定，請 15 分鐘後再試。");
            return View(model);
        }

        // 不透露是 Email 不存在還是密碼錯誤
        ModelState.AddModelError(string.Empty, "Email 或密碼不正確。");
        return View(model);
    }

    // ---------------- 登出 ----------------

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    // ---------------- 沒有權限 ----------------

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    // ---------------- 共用 ----------------

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home");

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
