using Microsoft.AspNetCore.Mvc;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class ContentModel(AdminService admin) : AdminPageModel(admin)
{
    [BindProperty] public string Kind { get; set; } = "";
    [BindProperty] public Guid ObjectId { get; set; }
    [BindProperty] public Guid CommunityId { get; set; }
    [BindProperty] public string? CurrentPassword { get; set; }
    public IReadOnlyList<AdminContentRow> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Модерация";
        await LoadAsync();
    }

    public Task<IActionResult> OnPostAsync()
        => Mutate(work => work.ModerateAsync(Kind, ObjectId, CommunityId), true, CurrentPassword);

    protected override async Task LoadAsync()
        => Items = await Admin.ExecuteAsync(User, work => work.ListContentAsync(), false, null, HttpContext.RequestAborted);
}
