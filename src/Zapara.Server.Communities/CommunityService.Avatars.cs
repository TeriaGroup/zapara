using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

public sealed partial class CommunityService
{
    // Used only by trusted modules through a host adapter, under their existing transaction.
    public Task<string> RequireAvatarAccessAsync(TrustedAccountContext context, Guid communityId, bool write, CancellationToken ct)
        => new CommunityRepository(context, configuration, ct).RequireAvatarAccessAsync(communityId, write);

    public Task<bool> ShareAvatarMembershipAsync(TrustedAccountContext context, Guid userId, CancellationToken ct)
        => new CommunityRepository(context, configuration, ct).ShareAvatarMembershipAsync(userId);
}

internal sealed partial class CommunityRepository
{
    internal async Task<string> RequireAvatarAccessAsync(Guid communityId, bool write)
    {
        if (write) await RequirePowerAsync(communityId, "channels");
        else await RequireMemberAsync(communityId);
        await using var command = Command($"SELECT group_id FROM {Schema}.catalog_maps WHERE community_id=@p0 ORDER BY group_id LIMIT 1", communityId);
        return await command.ExecuteScalarAsync(ct) as string ?? communityId.ToString("D");
    }

    internal Task<bool> ShareAvatarMembershipAsync(Guid userId) => ExistsAsync($"""
        SELECT 1 FROM {Schema}.memberships mine
        JOIN {Schema}.memberships peer ON peer.community_id=mine.community_id
        WHERE mine.user_id=@p0 AND peer.user_id=@p1 AND mine.status='active' AND peer.status='active'
        LIMIT 1
        """, UserId, userId);
}
