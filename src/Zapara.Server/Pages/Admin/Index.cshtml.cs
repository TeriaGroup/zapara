using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Zapara.Server.Pages.Admin;

public sealed class IndexModel : PageModel
{
    public void OnGet() => ViewData["Title"] = "Администрирование";
}
