using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public abstract class AdminPageModel(AdminService admin) : PageModel
{
    protected AdminService Admin { get; } = admin;
    public string? Error { get; set; }

    protected static string Message(AdminException exception) => exception.Code switch
    {
        "reauth_required" => "Повторная аутентификация обязательна.",
        "invalid_credentials" => "Неверные данные для входа.",
        "not_found" => "Объект не найден.",
        "conflict" or "revision_conflict" => "Конфликт данных.",
        "db_unavailable" => "Сервис временно недоступен.",
        _ => "Недопустимые данные."
    };

    protected async Task<IActionResult> Mutate(Func<AdminWork, Task> operation, bool reauth = false, string? password = null)
    {
        try
        {
            await Admin.ExecuteAsync(User, operation, reauth, password, HttpContext.RequestAborted);
            return RedirectToPage();
        }
        catch (AdminException exception)
        {
            Error = Message(exception);
            await LoadAsync();
            return Page();
        }
    }

    protected virtual Task LoadAsync() => Task.CompletedTask;
}
