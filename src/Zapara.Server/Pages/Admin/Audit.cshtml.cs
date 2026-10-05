using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class AuditModel(AdminService admin) : AdminPageModel(admin)
{
    public IReadOnlyList<AdminAuditRow> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Журнал";
        await LoadAsync();
    }

    protected override async Task LoadAsync()
        => Items = await Admin.ExecuteAsync(User, work => work.ListAuditAsync(), false, null, HttpContext.RequestAborted);
}
