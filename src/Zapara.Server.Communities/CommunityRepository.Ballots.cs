using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    internal Task<BallotBoardResponse> BoardAsync(Guid communityId, Guid? topicId = null) => ReadBoardAsync(communityId, topicId);

    internal async Task<BallotBoardResponse> OpenHeadmanBallotAsync(Guid communityId, BallotDraftRequest request)
    {
        if (request is null) throw CommunityServiceException.InvalidRequest();
        await RequirePowerAsync(communityId, "ballots");
        await RequireBallotPublishTopicAsync(communityId, request.TopicId);
        await CloseExpiredAsync(communityId);
        if (await ActiveCountAsync(communityId) >= BallotRules.ActiveLimit) throw CommunityServiceException.InvalidRequest();
        await InsertBallotAsync(communityId, request.Question, request.Options, "headman", "open", Now.AddDays(request.Days), UserId, request.TopicId);
        return await ReadBoardAsync(communityId);
    }

    internal async Task<BallotBoardResponse> ProposeBallotAsync(Guid communityId, BallotDraftRequest request)
    {
        if (request is null) throw CommunityServiceException.InvalidRequest();
        await RequireMemberAsync(communityId);
        await RequireBallotPublishTopicAsync(communityId, request.TopicId);
        await CloseExpiredAsync(communityId);
        if (await ActiveCountAsync(communityId) >= BallotRules.ActiveLimit) throw CommunityServiceException.InvalidRequest();
        var pending = await ScalarAsync($"""
            SELECT count(*)::int FROM {Msg}.ballots
            WHERE community_id=@p0 AND created_by=@p1 AND origin='collective' AND status='collecting'
            """, communityId, UserId);
        if (pending >= 1) throw CommunityServiceException.InvalidRequest();
        var id = await InsertBallotAsync(communityId, request.Question, request.Options, "collective", "collecting", Now.AddDays(request.Days), UserId, request.TopicId);
        await ExecuteAsync($"""
            INSERT INTO {Msg}.ballot_support(ballot_id,user_id,created_at) VALUES(@p0,@p1,@p2)
            ON CONFLICT DO NOTHING
            """, id, UserId, Now);
        return await ReadBoardAsync(communityId);
    }

    internal async Task<BallotBoardResponse> SupportBallotAsync(Guid communityId, Guid ballotId)
    {
        await RequireMemberAsync(communityId);
        await CloseExpiredAsync(communityId);
        var row = await FindBallotAsync(communityId, ballotId) ?? throw CommunityServiceException.NotFound();
        if (row.Status == "closed") throw CommunityServiceException.Conflict("poll_closed");
        if (row.Status == "collecting")
            await ExecuteAsync($"""
                INSERT INTO {Msg}.ballot_support(ballot_id,user_id,created_at) VALUES(@p0,@p1,@p2)
                ON CONFLICT DO NOTHING
                """, ballotId, UserId, Now);
        return await ReadBoardAsync(communityId);
    }

    internal async Task<BallotBoardResponse> VoteBallotAsync(Guid communityId, Guid ballotId, Guid optionId)
    {
        await RequireMemberAsync(communityId);
        await CloseExpiredAsync(communityId);
        var row = await FindBallotAsync(communityId, ballotId) ?? throw CommunityServiceException.NotFound();
        if (row.Status == "closed" || row.Deadline <= Now) throw CommunityServiceException.Conflict("poll_closed");
        if (row.Status != "open") throw CommunityServiceException.InvalidRequest();
        if (!await ExistsAsync($"SELECT option_id FROM {Msg}.ballot_options WHERE option_id=@p0 AND ballot_id=@p1", optionId, ballotId))
            throw CommunityServiceException.InvalidRequest();
        await ExecuteAsync($"""
            INSERT INTO {Msg}.ballot_votes(ballot_id,user_id,option_id,updated_at)
            VALUES(@p0,@p1,@p2,@p3)
            ON CONFLICT (ballot_id, user_id) DO UPDATE
            SET option_id=EXCLUDED.option_id, updated_at=EXCLUDED.updated_at
            """, ballotId, UserId, optionId, Now);
        return await ReadBoardAsync(communityId);
    }

    internal async Task<BallotBoardResponse> CloseBallotAsync(Guid communityId, Guid ballotId)
    {
        await RequirePowerAsync(communityId, "close");
        if (await FindBallotAsync(communityId, ballotId) is null) throw CommunityServiceException.NotFound();
        if (await ExistsAsync($"SELECT ballot_id FROM {Msg}.ballot_effects WHERE ballot_id=@p0", ballotId))
            throw CommunityServiceException.Forbidden();
        await ExecuteAsync($"""
            UPDATE {Msg}.ballots SET status='closed'
            WHERE ballot_id=@p0 AND community_id=@p1 AND status IN ('collecting','open')
            """, ballotId, communityId);
        return await ReadBoardAsync(communityId);
    }

    private async Task<Guid> InsertBallotAsync(Guid communityId, string question, IReadOnlyList<string> options, string origin, string status, DateTimeOffset deadline, Guid createdBy, Guid? topicId = null)
    {
        var id = Guid.NewGuid();
        DateTimeOffset? opened = status == "open" ? Now : null;
        await ExecuteAsync($"""
            INSERT INTO {Msg}.ballots(ballot_id,community_id,topic_id,question,origin,status,deadline_at,opened_at,created_by,created_at)
            VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)
            """, id, communityId, topicId, question, origin, status, deadline, opened, createdBy, Now);
        for (var i = 0; i < options.Count; i++)
            await ExecuteAsync($"""
                INSERT INTO {Msg}.ballot_options(option_id,ballot_id,label,ordinal)
                VALUES(@p0,@p1,@p2,@p3)
                """, Guid.NewGuid(), id, options[i], i);
        return id;
    }

    private async Task<BallotBoardResponse> ReadBoardAsync(Guid communityId, Guid? topicId = null)
    {
        var role = await RequireMemberAsync(communityId);
        await ValidateBallotTopicAsync(communityId, topicId);
        await CloseExpiredAsync(communityId);
        await ApplyDueAsync(communityId);
        var members = await ScalarAsync($"SELECT count(*)::int FROM {Schema}.memberships WHERE community_id=@p0 AND status='active'", communityId);
        var need = BallotRules.SupportersNeeded(members);
        await ExecuteAsync($"""
            UPDATE {Msg}.ballots AS ballot
            SET status='open', opened_at=COALESCE(ballot.opened_at, @p1)
            WHERE ballot.community_id=@p0 AND ballot.status='collecting' AND ballot.deadline_at > @p1
              AND (SELECT count(*) FROM {Msg}.ballot_support AS support
                   JOIN {Schema}.memberships AS member ON member.community_id=ballot.community_id AND member.user_id=support.user_id AND member.status='active'
                   WHERE support.ballot_id=ballot.ballot_id) >= @p2
            """, communityId, Now, need);
        var rows = new List<BallotRow>();
        var topicClause = topicId is null ? "" : " AND topic_id=@p1";
        object?[] filterArguments = topicId is Guid selected ? [communityId, selected] : [communityId];
        await using (var command = Command($"""
            SELECT ballot_id, question, origin, status, deadline_at, topic_id
            FROM {Msg}.ballots
            WHERE community_id=@p0 AND status IN ('open','collecting'){topicClause}
            ORDER BY CASE status WHEN 'open' THEN 0 ELSE 1 END, created_at DESC
            """, filterArguments))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) rows.Add(ReadRow(reader));
        await using (var command = Command($"""
            SELECT ballot_id, question, origin, status, deadline_at, topic_id
            FROM {Msg}.ballots
            WHERE community_id=@p0 AND status='closed'{topicClause}
            ORDER BY created_at DESC
            LIMIT 8
            """, filterArguments))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) rows.Add(ReadRow(reader));
        var support = new Dictionary<Guid, (int Count, bool Mine)>();
        var options = new Dictionary<Guid, List<BallotOptionResponse>>();
        var effects = new Dictionary<Guid, (string Kind, string Outcome)>();
        if (rows.Count > 0)
        {
            var args = new object?[rows.Count + 1];
            var marks = new string[rows.Count];
            args[0] = UserId;
            for (var i = 0; i < rows.Count; i++)
            {
                args[i + 1] = rows[i].Id;
                marks[i] = "@p" + (i + 1);
            }
            var list = string.Join(",", marks);
            await using (var command = Command($"""
                SELECT support.ballot_id, count(*)::int, bool_or(support.user_id=@p0)
                FROM {Msg}.ballot_support AS support
                JOIN {Msg}.ballots AS ballot ON ballot.ballot_id=support.ballot_id
                JOIN {Schema}.memberships AS member ON member.community_id=ballot.community_id AND member.user_id=support.user_id AND member.status='active'
                WHERE support.ballot_id IN ({list})
                GROUP BY support.ballot_id
                """, args))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    support[reader.GetGuid(0)] = (reader.GetInt32(1), !reader.IsDBNull(2) && reader.GetBoolean(2));
            await using (var command = Command($"""
                SELECT choice.ballot_id, choice.option_id, choice.label,
                       (SELECT count(*)::int FROM {Msg}.ballot_votes AS vote
                        JOIN {Msg}.ballots AS ballot ON ballot.ballot_id=vote.ballot_id
                        JOIN {Schema}.memberships AS member ON member.community_id=ballot.community_id AND member.user_id=vote.user_id AND member.status='active'
                        WHERE vote.ballot_id=choice.ballot_id AND vote.option_id=choice.option_id),
                       EXISTS(SELECT 1 FROM {Msg}.ballot_votes AS vote
                        WHERE vote.ballot_id=choice.ballot_id AND vote.option_id=choice.option_id AND vote.user_id=@p0)
                FROM {Msg}.ballot_options AS choice
                WHERE choice.ballot_id IN ({list})
                ORDER BY choice.ballot_id, choice.ordinal, choice.option_id
                """, args))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    var ballotId = reader.GetGuid(0);
                    if (!options.TryGetValue(ballotId, out var bucket)) options[ballotId] = bucket = new();
                    bucket.Add(new(reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), reader.GetBoolean(4)));
                }
            await using (var command = Command($"SELECT ballot_id, kind, outcome FROM {Msg}.ballot_effects WHERE ballot_id IN ({list})", args))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    effects[reader.GetGuid(0)] = (reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2));
        }
        var ballots = new List<BallotResponse>(rows.Count);
        foreach (var row in rows)
        {
            support.TryGetValue(row.Id, out var voice);
            options.TryGetValue(row.Id, out var choices);
            effects.TryGetValue(row.Id, out var effect);
            ballots.Add(new(row.Id, row.Question, row.Origin, row.Status, row.Deadline, voice.Count, need, voice.Mine, choices ?? new List<BallotOptionResponse>(), effect.Kind ?? "", effect.Outcome ?? "", row.TopicId));
        }
        var canOpen = role == "headman" || await HasPowerAsync(communityId, "ballots");
        var canClose = role == "headman" || await HasPowerAsync(communityId, "close");
        return new(role == "headman", canOpen, canClose, members, need, ballots);
    }

    private Task CloseExpiredAsync(Guid communityId) => ExecuteAsync($"""
        UPDATE {Msg}.ballots SET status='closed'
        WHERE community_id=@p0 AND status IN ('collecting','open') AND deadline_at <= @p1
        """, communityId, Now);

    private Task<int> ActiveCountAsync(Guid communityId) => ScalarAsync($"""
        SELECT count(*)::int FROM {Msg}.ballots
        WHERE community_id=@p0 AND status IN ('collecting','open')
        """, communityId);

    private async Task<(string Status, DateTimeOffset Deadline)?> FindBallotAsync(Guid communityId, Guid ballotId)
    {
        await using var command = Command($"SELECT status, deadline_at FROM {Msg}.ballots WHERE ballot_id=@p0 AND community_id=@p1", ballotId, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetString(0), AsUtc(reader.GetFieldValue<DateTimeOffset>(1)));
    }

    private static BallotRow ReadRow(NpgsqlDataReader reader) => new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), AsUtc(reader.GetFieldValue<DateTimeOffset>(4)), reader.IsDBNull(5) ? null : reader.GetGuid(5));

    private static DateTimeOffset AsUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private sealed record BallotRow(Guid Id, string Question, string Origin, string Status, DateTimeOffset Deadline, Guid? TopicId);
}
