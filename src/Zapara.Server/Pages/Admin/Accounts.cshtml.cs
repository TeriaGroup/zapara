using Microsoft.AspNetCore.Mvc;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class AccountsModel(AdminService admin) : AdminPageModel(admin)
{
    [BindProperty] public Guid UserId { get; set; }
    [BindProperty] public Guid FamilyId { get; set; }
    [BindProperty] public string? CurrentPassword { get; set; }
    public IReadOnlyList<AdminAccountRow> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Аккаунты";
        await LoadAsync();
    }

    public Task<IActionResult> OnPostDisableAsync()
        => Mutate(work => work.DisableAccountAsync(UserId), true, CurrentPassword);

    public Task<IActionResult> OnPostRevokeAsync()
        => Mutate(work => work.RevokeFamilyAsync(FamilyId), true, CurrentPassword);

    protected override async Task LoadAsync()
        => Items = await Admin.ExecuteAsync(User, work => work.ListAccountsAsync(), false, null, HttpContext.RequestAborted);
}
