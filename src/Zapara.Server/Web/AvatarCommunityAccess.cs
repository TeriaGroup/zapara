using Zapara.Server.Accounts;
using Zapara.Server.Communities;
using Zapara.Server.Social;

namespace Zapara.Server.Web;

internal sealed class AvatarCommunityAccess(CommunityService service) : IAvatarCommunityAccess
{
    public async Task<string> RequireAsync(TrustedAccountContext context, Guid communityId, bool write, CancellationToken ct)
    {
        try { return await service.RequireAvatarAccessAsync(context, communityId, write, ct); }
        catch (CommunityServiceException exception) when (exception.Status is 403 or 404)
        { throw new SocialException(write ? exception.Status : 404, write ? exception.Code : "not_found"); }
    }

    public Task<bool> ShareMembershipAsync(TrustedAccountContext context, Guid userId, CancellationToken ct)
        => service.ShareAvatarMembershipAsync(context, userId, ct);
}
