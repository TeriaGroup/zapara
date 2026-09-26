namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    private readonly HashSet<Guid> initializedSpaces = [];

    // Installation records groups with existing messenger state as legacy. Empty
    // catalog groups get starters too; timestamps and clock skew do not decide this.
    private async Task EnsureStarterTopicsAsync(Guid communityId)
    {
        if (initializedSpaces.Contains(communityId)) return;
        object? created;
        await using (var command = Command($"""
            INSERT INTO {Msg}.group_space_state(community_id,starter_set)
            VALUES(@p0,true)
            ON CONFLICT(community_id) DO NOTHING
            RETURNING starter_set
            """, communityId))
            created = await command.ExecuteScalarAsync(ct);
        if (created is true)
        {
            var accessSnapshot = await CurrentAccessSnapshotAsync(communityId);
            foreach (var item in new[]
            {
                (Title: "Важное", Icon: "megaphone", Kind: "chat", Template: "announcements", Position: 1),
                (Title: "Опросы", Icon: "vote", Kind: "ballots", Template: "polls", Position: 2)
            })
            {
                var id = Guid.NewGuid();
                var added = await ExecuteCountAsync($"""
                    INSERT INTO {Msg}.group_topics(topic_id,community_id,title,icon,kind,template,position,created_by,created_at,access_snapshot)
                    VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,NULL,@p7,@p8)
                    ON CONFLICT DO NOTHING
                    """, id, communityId, item.Title, item.Icon, item.Kind, item.Template, item.Position, Now, accessSnapshot);
                if (added == 1) await ManagementAuditAsync(communityId, "topic.initialized", id);
            }
        }
        initializedSpaces.Add(communityId);
    }

    private async Task<string> GeneralTitleAsync(Guid communityId)
    {
        await using var command = Command($"SELECT starter_set FROM {Msg}.group_space_state WHERE community_id=@p0", communityId);
        return await command.ExecuteScalarAsync(ct) is true ? "Чатик" : GroupTopicNames.GeneralTitle;
    }
}
