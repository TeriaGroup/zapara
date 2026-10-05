using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

[AllowAnonymous]
[EnableRateLimiting("account-login")]
public sealed class LoginModel(AdminAuthService auth) : PageModel
{
    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(Safe(returnUrl));
        ViewData["Title"] = "Вход";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ViewData["Title"] = "Вход";
        try
        {
            var identity = await auth.AuthenticateAsync(Username, Password, HttpContext.RequestAborted);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString("D")),
                new Claim("admin", "platform"),
                new Claim("cv", identity.CredentialVersion.ToString())
            ], AdminDefaults.Scheme));
            await HttpContext.SignInAsync(AdminDefaults.Scheme, principal, new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(AdminDefaults.SessionLifetime)
            });
            return LocalRedirect(Safe(returnUrl));
        }
        catch (AdminException)
        {
            Error = "Неверные данные для входа.";
            return Page();
        }
    }

    private string Safe(string? returnUrl) => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Admin";
}
