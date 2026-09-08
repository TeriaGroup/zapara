using Microsoft.AspNetCore.Mvc;
using Zapara.Server.Admin;

namespace Zapara.Server.Pages.Admin;

public sealed class MembershipsModel(AdminService admin) : AdminPageModel(admin)
{
    [BindProperty] public Guid CommunityId { get; set; }
    [BindProperty] public Guid RequestId { get; set; }
    public IReadOnlyList<AdminJoinRow> Items { get; set; } = [];

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "Заявки";
        await LoadAsync();
    }

    public Task<IActionResult> OnPostAcceptAsync()
        => Mutate(work => work.AcceptJoinAsync(CommunityId, RequestId));

    public Task<IActionResult> OnPostRejectAsync()
        => Mutate(work => work.RejectJoinAsync(CommunityId, RequestId));

    protected override async Task LoadAsync()
        => Items = await Admin.ExecuteAsync(User, work => work.ListJoinRequestsAsync(), false, null, HttpContext.RequestAborted);
}
