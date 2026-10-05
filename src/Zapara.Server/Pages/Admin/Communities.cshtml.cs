using Microsoft.AspNetCore.Mvc;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class CommunitiesModel(AdminService admin) : AdminPageModel(admin)
{
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Description { get; set; } = "";
    [BindProperty] public Guid CommunityId { get; set; }
    [BindProperty] public string GroupId { get; set; } = "";
    [BindProperty] public string GroupName { get; set; } = "";
    public IReadOnlyList<AdminCommunityRow> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Сообщества";
        await LoadAsync();
    }

    public Task<IActionResult> OnPostCreateAsync()
        => Mutate(work => work.CreateCommunityAsync(Name, Description));

    public Task<IActionResult> OnPostMapAsync()
        => Mutate(work => work.MapCatalogAsync(CommunityId, GroupId, GroupName));

    protected override async Task LoadAsync()
        => Items = await Admin.ExecuteAsync(User, work => work.ListCommunitiesAsync(), false, null, HttpContext.RequestAborted);
}
