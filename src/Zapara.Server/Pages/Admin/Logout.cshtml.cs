using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet() => StatusCode(405);

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(AdminDefaults.Scheme);
        return RedirectToPage("/Admin/Login");
    }
}
