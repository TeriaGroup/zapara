using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    private sealed record HomeworkRow(HomeworkResponse Item, Guid? Author, bool Completed, long CompletionRevision);
    private sealed record HomeworkAccess(SpaceActor Actor, IReadOnlyDictionary<Guid, string[]> Topics);

    internal async Task<HomeworkResponse> PublishHomeworkAsync(Guid communityId, HomeworkUpsert request)
    {
        await RequireHomeworkPublisherAsync(communityId, request.TopicId);
        return await CreateHomeworkAsync(communityId, request);
    }

    internal async Task<HomeworkResponse> ShareHomeworkAsync(Guid communityId, HomeworkUpsert request)
    {
        await RequireMemberAsync(communityId);
        if (request.TopicId is Guid topic) await RequireTopicPermissionAsync(communityId, topic, "homework");
        return await CreateHomeworkAsync(communityId, request);
    }

    private async Task<HomeworkResponse> CreateHomeworkAsync(Guid communityId, HomeworkUpsert request)
    {
        if (request.ExpectedRevision != 0) throw CommunityServiceException.Conflict("revision_conflict");
        if (string.IsNullOrWhiteSpace(request.Body)) throw CommunityServiceException.InvalidRequest();
        var audience = request.Audience ?? HomeworkAudience.All;
        var fingerprint = Convert.ToHexString(SHA256.HashData(CommunityJson.Serialize(new
        {
            request.Title, request.Body, request.DeadlineAt, request.TopicId, Audience = audience
        })));
        if (request.OperationId is Guid operation)
        {
            Guid? previous = null;
            await using (var command = Command($"""
                SELECT homework_id,payload_hash FROM {Msg}.group_homework_operations
                WHERE community_id=@p0 AND author_id=@p1 AND operation_id=@p2
                """, communityId, UserId, operation))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                if (await reader.ReadAsync(ct))
                {
                    if (reader.GetString(1) != fingerprint) throw CommunityServiceException.Conflict("revision_conflict");
                    previous = reader.GetGuid(0);
                }
            // Membership/topic access is checked for every retry. Current content may have been edited since creation.
            if (previous is Guid id) return await GetHomeworkRowAsync(communityId, id, false);
        }
        await ValidateHomeworkAudienceAsync(communityId, audience);
        var homeworkId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Schema}.shared_homework(homework_id,community_id,title,body,revision,created_by,created_at,updated_at)
            VALUES(@p0,@p1,@p2,@p3,1,@p4,@p5,@p5)
            """, homeworkId, communityId, request.Title, request.Body, UserId, Now);
        await SaveHomeworkDetailsAsync(homeworkId, request.DeadlineAt, request.TopicId, audience);
        if (request.OperationId is Guid operationId)
            await ExecuteAsync($"""
                INSERT INTO {Msg}.group_homework_operations(community_id,author_id,operation_id,homework_id,payload_hash)
                VALUES(@p0,@p1,@p2,@p3,@p4)
                """, communityId, UserId, operationId, homeworkId, fingerprint);
        await AuditAsync(communityId, "homework_published", "shared_homework", homeworkId);
        return await GetHomeworkRowAsync(communityId, homeworkId, false);
    }

    internal async Task<HomeworkResponse> UpdateHomeworkAsync(Guid communityId, Guid homeworkId, HomeworkUpsert request)
    {
        await RequireMemberAsync(communityId);
        var current = await GetHomeworkRowAsync(communityId, homeworkId, true);
        await RequireHomeworkPublisherAsync(communityId, current.TopicId);
        if (request.TopicId is not null && request.TopicId != current.TopicId) throw CommunityServiceException.InvalidRequest();
        if (request.OperationId is not null) throw CommunityServiceException.InvalidRequest();
        if (request.ExpectedRevision != current.Revision) throw CommunityServiceException.Conflict("revision_conflict");
        var audience = request.Audience ?? current.Audience;
        // Old editors omit audience; preserve it even if a selected role/member has since left.
        if (request.Audience is not null) await ValidateHomeworkAudienceAsync(communityId, audience);
        await ExecuteAsync($"""
            UPDATE {Schema}.shared_homework SET title=@p0,body=@p1,revision=@p2,updated_at=@p3 WHERE homework_id=@p4
            """, request.Title, request.Body, current.Revision + 1, Now, homeworkId);
        await SaveHomeworkDetailsAsync(homeworkId, request.DeadlineAt, current.TopicId, audience);
        await AuditAsync(communityId, "homework_updated", "shared_homework", homeworkId);
        return await GetHomeworkRowAsync(communityId, homeworkId, false);
    }

    internal async Task<IReadOnlyList<GroupHomeworkCopyResponse>> ListHomeworkCopiesAsync(Guid communityId, Guid? topicId = null)
    {
        await RequireMemberAsync(communityId);
        if (topicId is Guid topic) await RequireTopicPermissionAsync(communityId, topic, "read");
        var access = await HomeworkAccessAsync(communityId);
        var result = new List<GroupHomeworkCopyResponse>();
        foreach (var row in await HomeworkRowsAsync(communityId))
        {
            if (topicId is not null && row.Item.TopicId != topicId) continue;
            var item = VisibleHomework(row, access);
            if (item is not null) result.Add(new(item.HomeworkId, item.Title, item.Body, item.Revision,
                row.Completed, row.CompletionRevision, item.DeadlineAt, item.TopicId, item.Audience, item.CanEdit, item.CanComplete));
        }
        return result;
    }

    internal async Task<IReadOnlyList<HomeworkResponse>> ListHomeworkAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var access = await HomeworkAccessAsync(communityId);
        return (await HomeworkRowsAsync(communityId)).Select(row => VisibleHomework(row, access)).OfType<HomeworkResponse>()
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.HomeworkId).ToArray();
    }

    internal async Task<HomeworkResponse> GetHomeworkAsync(Guid communityId, Guid homeworkId)
    {
        await RequireMemberAsync(communityId);
        return await GetHomeworkRowAsync(communityId, homeworkId, false);
    }

    private async Task<HomeworkResponse> GetHomeworkRowAsync(Guid communityId, Guid homeworkId, bool locked)
    {
        var access = await HomeworkAccessAsync(communityId);
        var row = (await HomeworkRowsAsync(communityId, homeworkId, locked)).SingleOrDefault();
        return row is null ? throw CommunityServiceException.NotFound()
            : VisibleHomework(row, access) ?? throw CommunityServiceException.NotFound();
    }

    private async Task<List<HomeworkRow>> HomeworkRowsAsync(Guid communityId, Guid? homeworkId = null, bool locked = false)
    {
        var rows = new List<HomeworkRow>();
        await using var command = Command($"""
            SELECT h.homework_id,h.community_id,h.title,h.body,h.revision,h.created_at,h.updated_at,
                   d.deadline_at,d.topic_id,d.audience,h.created_by,COALESCE(c.completed,false),COALESCE(c.revision,0)
            FROM {Schema}.shared_homework h
            LEFT JOIN {Msg}.group_homework_details d ON d.homework_id=h.homework_id
            LEFT JOIN {Schema}.shared_homework_completion c ON c.homework_id=h.homework_id AND c.user_id=@p1
            WHERE h.community_id=@p0 {(homeworkId.HasValue ? "AND h.homework_id=@p2" : "")}
            ORDER BY h.created_at DESC,h.homework_id {(locked ? "FOR UPDATE OF h" : "")}
            """, homeworkId.HasValue ? [communityId, UserId, homeworkId.Value] : [communityId, UserId]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var audience = reader.IsDBNull(9) ? HomeworkAudience.All : CommunityJson.Parse<HomeworkAudience>(Encoding.UTF8.GetBytes(reader.GetString(9)));
            rows.Add(new(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetGuid(8), audience),
                reader.IsDBNull(10) ? null : reader.GetGuid(10), reader.GetBoolean(11), reader.GetInt64(12)));
        }
        return rows;
    }

    private async Task<HomeworkAccess> HomeworkAccessAsync(Guid communityId)
    {
        var actor = await SpaceActorAsync(communityId, UserId);
        var permissions = new Dictionary<Guid, string[]>();
        foreach (var topic in await SpaceTopicsAsync(communityId))
            permissions[topic.Id] = await TopicPermissionsAsync(communityId, topic, actor);
        return new(actor, permissions);
    }

    private HomeworkResponse? VisibleHomework(HomeworkRow row, HomeworkAccess access)
    {
        var item = row.Item;
        string[]? topicRights = null;
        if (item.TopicId is Guid topic && (!access.Topics.TryGetValue(topic, out topicRights) || !topicRights.Contains("read"))) return null;
        var manager = access.Actor.OfficialRole is "headman" or "curator" || access.Actor.Powers.Contains("homework");
        var canEdit = item.TopicId is null ? manager : topicRights!.Contains("homework");
        var recipient = item.Audience.Kind == "all" || item.Audience.UserIds.Contains(UserId) || item.Audience.RoleIds.Intersect(access.Actor.Roles).Any();
        if (!recipient && row.Author != UserId && !manager && !canEdit) return null;
        return new(item.HomeworkId, item.CommunityId, item.Title, item.Body, item.Revision, item.CreatedAt, item.UpdatedAt,
            item.DeadlineAt, item.TopicId, item.Audience, canEdit, recipient);
    }

    private async Task ValidateHomeworkAudienceAsync(Guid communityId, HomeworkAudience audience)
    {
        if (audience.Kind == "all") return;
        if (audience.RoleIds.Count > 0)
        {
            await using var roles = Command($"SELECT count(*)::int FROM {Msg}.group_roles WHERE community_id=@p0 AND role_id=ANY(@p1)", communityId, audience.RoleIds.ToArray());
            if ((int)(await roles.ExecuteScalarAsync(ct))! != audience.RoleIds.Count) throw CommunityServiceException.InvalidRequest();
        }
        if (audience.UserIds.Count > 0)
        {
            await using var people = Command($"""
                SELECT count(*)::int FROM {Schema}.memberships m JOIN {configuration.Accounts.QuotedSchema}.users u ON u.user_id=m.user_id
                WHERE m.community_id=@p0 AND m.user_id=ANY(@p1) AND m.status='active' AND u.status='active'
                """, communityId, audience.UserIds.ToArray());
            if ((int)(await people.ExecuteScalarAsync(ct))! != audience.UserIds.Count) throw CommunityServiceException.InvalidRequest();
        }
    }

    private Task SaveHomeworkDetailsAsync(Guid homeworkId, DateTimeOffset? deadline, Guid? topicId, HomeworkAudience audience)
        => ExecuteAsync($"""
            INSERT INTO {Msg}.group_homework_details(homework_id,deadline_at,topic_id,audience) VALUES(@p0,@p1,@p2,@p3)
            ON CONFLICT(homework_id) DO UPDATE SET deadline_at=EXCLUDED.deadline_at,audience=EXCLUDED.audience
            """, homeworkId, deadline, topicId, Encoding.UTF8.GetString(CommunityJson.Serialize(audience)));

    private async Task RequireHomeworkPublisherAsync(Guid communityId, Guid? topicId)
    {
        var role = await RequireMemberAsync(communityId);
        if (topicId is Guid topic) { await RequireTopicPermissionAsync(communityId, topic, "homework"); return; }
        if (role is not ("headman" or "curator") && !await HasPowerAsync(communityId, "homework")) throw CommunityServiceException.Forbidden();
    }

    internal async Task<CompletionResponse> UpsertCompletionAsync(Guid communityId, Guid homeworkId, CompletionUpsert request)
    {
        await RequireMemberAsync(communityId);
        if (!(await GetHomeworkRowAsync(communityId, homeworkId, false)).CanComplete) throw CommunityServiceException.Forbidden();
        long current = 0;
        await using (var command = Command($"SELECT revision FROM {Schema}.shared_homework_completion WHERE homework_id=@p0 AND user_id=@p1 FOR UPDATE", homeworkId, UserId))
        {
            var revision = await command.ExecuteScalarAsync(ct);
            if (revision is long value) current = value;
        }
        if (request.ExpectedRevision != current) throw CommunityServiceException.Conflict("revision_conflict");
        await ExecuteAsync($"""
            INSERT INTO {Schema}.shared_homework_completion(homework_id,user_id,completed,revision,updated_at)
            VALUES(@p0,@p1,@p2,@p3,@p4)
            ON CONFLICT(homework_id,user_id) DO UPDATE SET completed=@p2,revision=@p3,updated_at=@p4
            """, homeworkId, UserId, request.Completed, current + 1, Now);
        return new(homeworkId, request.Completed, current + 1, Now);
    }

    internal async Task<CompletionResponse> GetCompletionAsync(Guid communityId, Guid homeworkId)
    {
        await RequireMemberAsync(communityId);
        _ = await GetHomeworkRowAsync(communityId, homeworkId, false);
        await using var command = Command($"SELECT completed,revision,updated_at FROM {Schema}.shared_homework_completion WHERE homework_id=@p0 AND user_id=@p1", homeworkId, UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(homeworkId, reader.GetBoolean(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2))
            : new(homeworkId, false, 0, null);
    }
}
