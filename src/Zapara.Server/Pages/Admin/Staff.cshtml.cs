using Microsoft.AspNetCore.Mvc;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class StaffModel(AdminService admin) : AdminPageModel(admin)
{
    [BindProperty] public Guid CommunityId { get; set; }
    [BindProperty] public Guid UserId { get; set; }
    [BindProperty] public string Role { get; set; } = "headman";
    [BindProperty] public string? CurrentPassword { get; set; }
    public IReadOnlyList<AdminCommunityRow> Communities { get; set; } = [];

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Персонал";
        await LoadAsync();
    }

    public Task<IActionResult> OnPostAsync()
        => Mutate(work => work.AssignStaffAsync(CommunityId, UserId, Role), true, CurrentPassword);

    protected override async Task LoadAsync()
        => Communities = await Admin.ExecuteAsync(User, work => work.ListCommunitiesAsync(), false, null, HttpContext.RequestAborted);
}
