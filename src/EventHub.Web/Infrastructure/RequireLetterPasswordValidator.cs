using EventHub.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace EventHub.Web.Infrastructure;

/// <summary>密碼需至少包含一個英文字母（大小寫皆可）。</summary>
public class RequireLetterPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (password is not null && password.Any(char.IsAsciiLetter))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "PasswordRequiresLetter",
            Description = "密碼需包含英文字母。",
        }));
    }
}
