using Npgsql;
using Zapara.Contracts.Communities;

namespace Zapara.Server.Communities;

internal sealed partial class CommunityRepository
{
    internal async Task<HomeworkResponse> PublishHomeworkAsync(Guid communityId, HomeworkUpsert request)
    {
        await RequireStaffAsync(communityId);
        if (request.ExpectedRevision != 0) throw CommunityServiceException.Conflict("revision_conflict");
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Schema}.shared_homework(homework_id,community_id,title,body,revision,created_by,created_at,updated_at)
            VALUES(@p0,@p1,@p2,@p3,1,@p4,@p5,@p5)
            """, id, communityId, request.Title, request.Body, UserId, Now);
        await AuditAsync(communityId, "homework_published", "shared_homework", id);
        return new(id, communityId, request.Title, request.Body, 1, Now, Now);
    }
    internal async Task<HomeworkResponse> UpdateHomeworkAsync(Guid communityId, Guid homeworkId, HomeworkUpsert request)
    {
        await RequireStaffAsync(communityId);
        var current = await GetHomeworkRowAsync(communityId, homeworkId, true);
        if (request.ExpectedRevision != current.Revision) throw CommunityServiceException.Conflict("revision_conflict");
        var revision = current.Revision + 1;
        await ExecuteAsync($"""
            UPDATE {Schema}.shared_homework SET title=@p0,body=@p1,revision=@p2,updated_at=@p3
            WHERE homework_id=@p4
            """, request.Title, request.Body, revision, Now, homeworkId);
        await AuditAsync(communityId, "homework_updated", "shared_homework", homeworkId);
        return new(homeworkId, communityId, request.Title, request.Body, revision, current.CreatedAt, Now);
    }
    internal async Task<IReadOnlyList<HomeworkResponse>> ListHomeworkAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var list = new List<HomeworkResponse>();
        await using var command = Command($"""
            SELECT homework_id,community_id,title,body,revision,created_at,updated_at
            FROM {Schema}.shared_homework WHERE community_id=@p0 ORDER BY created_at, homework_id
            """, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(ReadHomework(reader));
        return list;
    }
    internal async Task<HomeworkResponse> GetHomeworkAsync(Guid communityId, Guid homeworkId)
    {
        await RequireMemberAsync(communityId);
        return await GetHomeworkRowAsync(communityId, homeworkId, false);
    }
    private async Task<HomeworkResponse> GetHomeworkRowAsync(Guid communityId, Guid homeworkId, bool locked)
    {
        await using var command = Command($"""
            SELECT homework_id,community_id,title,body,revision,created_at,updated_at
            FROM {Schema}.shared_homework WHERE homework_id=@p0 AND community_id=@p1
            {(locked ? "FOR UPDATE" : "")}
            """, homeworkId, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
        return ReadHomework(reader);
    }
    private static HomeworkResponse ReadHomework(NpgsqlDataReader reader)
        => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
            reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6));
    internal async Task<CompletionResponse> UpsertCompletionAsync(Guid communityId, Guid homeworkId, CompletionUpsert request)
    {
        await RequireMemberAsync(communityId);
        _ = await GetHomeworkRowAsync(communityId, homeworkId, false);
        long current = 0;
        bool exists;
        await using (var command = Command($"""
            SELECT revision FROM {Schema}.shared_homework_completion
            WHERE homework_id=@p0 AND user_id=@p1 FOR UPDATE
            """, homeworkId, UserId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            exists = await reader.ReadAsync(ct);
            if (exists) current = reader.GetInt64(0);
        }
        if (request.ExpectedRevision != current) throw CommunityServiceException.Conflict("revision_conflict");
        var revision = current + 1;
        await ExecuteAsync($"""
            INSERT INTO {Schema}.shared_homework_completion(homework_id,user_id,completed,revision,updated_at)
            VALUES(@p0,@p1,@p2,@p3,@p4)
            ON CONFLICT (homework_id,user_id) DO UPDATE SET completed=@p2, revision=@p3, updated_at=@p4
            """, homeworkId, UserId, request.Completed, revision, Now);
        return new(homeworkId, request.Completed, revision, Now);
    }
    internal async Task<CompletionResponse> GetCompletionAsync(Guid communityId, Guid homeworkId)
    {
        await RequireMemberAsync(communityId);
        _ = await GetHomeworkRowAsync(communityId, homeworkId, false);
        await using var command = Command($"""
            SELECT completed,revision,updated_at FROM {Schema}.shared_homework_completion
            WHERE homework_id=@p0 AND user_id=@p1
            """, homeworkId, UserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return new(homeworkId, false, 0, null);
        return new(homeworkId, reader.GetBoolean(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2));
    }
    internal async Task<AnnouncementResponse> PublishAnnouncementAsync(Guid communityId, AnnouncementUpsert request)
    {
        await RequireStaffAsync(communityId);
        if (request.ExpectedRevision != 0) throw CommunityServiceException.Conflict("revision_conflict");
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Schema}.announcements(announcement_id,community_id,title,body,revision,created_by,created_at,updated_at)
            VALUES(@p0,@p1,@p2,@p3,1,@p4,@p5,@p5)
            """, id, communityId, request.Title, request.Body, UserId, Now);
        await AuditAsync(communityId, "announcement_published", "announcement", id);
        return new(id, communityId, request.Title, request.Body, 1, Now, Now);
    }
    internal async Task<AnnouncementResponse> UpdateAnnouncementAsync(Guid communityId, Guid announcementId, AnnouncementUpsert request)
    {
        await RequireStaffAsync(communityId);
        DateTimeOffset createdAt;
        long revision;
        await using (var command = Command($"""
            SELECT revision,created_at FROM {Schema}.announcements
            WHERE announcement_id=@p0 AND community_id=@p1 FOR UPDATE
            """, announcementId, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            revision = reader.GetInt64(0);
            createdAt = reader.GetFieldValue<DateTimeOffset>(1);
        }
        if (request.ExpectedRevision != revision) throw CommunityServiceException.Conflict("revision_conflict");
        revision++;
        await ExecuteAsync($"""
            UPDATE {Schema}.announcements SET title=@p0,body=@p1,revision=@p2,updated_at=@p3 WHERE announcement_id=@p4
            """, request.Title, request.Body, revision, Now, announcementId);
        await AuditAsync(communityId, "announcement_updated", "announcement", announcementId);
        return new(announcementId, communityId, request.Title, request.Body, revision, createdAt, Now);
    }
    internal async Task<IReadOnlyList<AnnouncementResponse>> ListAnnouncementsAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var list = new List<AnnouncementResponse>();
        await using var command = Command($"""
            SELECT announcement_id,community_id,title,body,revision,created_at,updated_at
            FROM {Schema}.announcements WHERE community_id=@p0 ORDER BY created_at, announcement_id
            """, communityId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6)));
        return list;
    }
    internal async Task<PollResponse> PublishPollAsync(Guid communityId, PollUpsert request)
    {
        await RequireStaffAsync(communityId);
        if (request.ExpectedRevision != 0) throw CommunityServiceException.Conflict("revision_conflict");
        if (request.DeadlineAt <= Now) throw CommunityServiceException.InvalidRequest();
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO {Schema}.polls(poll_id,community_id,question,deadline_at,revision,created_by,created_at,updated_at)
            VALUES(@p0,@p1,@p2,@p3,1,@p4,@p5,@p5)
            """, id, communityId, request.Question, request.DeadlineAt, UserId, Now);
        var options = new List<PollOptionResponse>();
        for (var i = 0; i < request.Options.Count; i++)
        {
            var optionId = Guid.NewGuid();
            await ExecuteAsync($"""
                INSERT INTO {Schema}.poll_options(option_id,poll_id,label,ordinal) VALUES(@p0,@p1,@p2,@p3)
                """, optionId, id, request.Options[i], i + 1);
            options.Add(new(optionId, request.Options[i], i + 1));
        }
        await AuditAsync(communityId, "poll_published", "poll", id);
        return new(id, communityId, request.Question, request.DeadlineAt, 1, options);
    }
    internal async Task<IReadOnlyList<PollResponse>> ListPollsAsync(Guid communityId)
    {
        await RequireMemberAsync(communityId);
        var polls = new List<(Guid Id, Guid CommunityId, string Question, DateTimeOffset Deadline, long Revision)>();
        await using (var command = Command($"""
            SELECT poll_id,community_id,question,deadline_at,revision
            FROM {Schema}.polls WHERE community_id=@p0 ORDER BY created_at, poll_id
            """, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                polls.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetInt64(4)));
        var result = new List<PollResponse>();
        foreach (var poll in polls) result.Add(new(poll.Id, poll.CommunityId, poll.Question, poll.Deadline, poll.Revision, await OptionsAsync(poll.Id)));
        return result;
    }
    internal async Task<PollResponse> GetPollAsync(Guid communityId, Guid pollId)
    {
        await RequireMemberAsync(communityId);
        return await LoadPollAsync(communityId, pollId, false);
    }
    private async Task<PollResponse> LoadPollAsync(Guid communityId, Guid pollId, bool locked)
    {
        Guid id;
        string question;
        DateTimeOffset deadline;
        long revision;
        await using (var command = Command($"""
            SELECT poll_id,question,deadline_at,revision FROM {Schema}.polls
            WHERE poll_id=@p0 AND community_id=@p1 {(locked ? "FOR UPDATE" : "")}
            """, pollId, communityId))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw CommunityServiceException.NotFound();
            id = reader.GetGuid(0);
            question = reader.GetString(1);
            deadline = reader.GetFieldValue<DateTimeOffset>(2);
            revision = reader.GetInt64(3);
        }
        return new(id, communityId, question, deadline, revision, await OptionsAsync(id));
    }
    private async Task<IReadOnlyList<PollOptionResponse>> OptionsAsync(Guid pollId)
    {
        var list = new List<PollOptionResponse>();
        await using var command = Command($"""
            SELECT option_id,label,ordinal FROM {Schema}.poll_options WHERE poll_id=@p0 ORDER BY ordinal
            """, pollId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2)));
        return list;
    }
    internal async Task<VoteResponse> VoteAsync(Guid communityId, Guid pollId, VoteRequest request)
    {
        await RequireMemberAsync(communityId);
        var poll = await LoadPollAsync(communityId, pollId, true);
        if (Now >= poll.DeadlineAt) throw CommunityServiceException.Conflict("poll_closed");
        if (poll.Options.All(o => o.OptionId != request.OptionId)) throw CommunityServiceException.InvalidRequest();
        if (await ExistsAsync($"SELECT option_id FROM {Schema}.votes WHERE poll_id=@p0 AND user_id=@p1", pollId, UserId))
            throw CommunityServiceException.Conflict("already_voted");
        try
        {
            await ExecuteAsync($"""
                INSERT INTO {Schema}.votes(poll_id,user_id,option_id,created_at) VALUES(@p0,@p1,@p2,@p3)
                """, pollId, UserId, request.OptionId, Now);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw CommunityServiceException.Conflict("already_voted"); }
        return new(pollId, request.OptionId, Now);
    }
    internal async Task<PollResultsResponse> ResultsAsync(Guid communityId, Guid pollId)
    {
        await RequireMemberAsync(communityId);
        _ = await LoadPollAsync(communityId, pollId, false);
        var options = new List<PollOptionResult>();
        var total = 0;
        await using var command = Command($"""
            SELECT o.option_id,o.label,count(v.user_id)::int
            FROM {Schema}.poll_options o
            LEFT JOIN {Schema}.votes v ON v.poll_id=o.poll_id AND v.option_id=o.option_id
            WHERE o.poll_id=@p0
            GROUP BY o.option_id,o.label,o.ordinal
            ORDER BY o.ordinal
            """, pollId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var votes = reader.GetInt32(2);
            total += votes;
            options.Add(new(reader.GetGuid(0), reader.GetString(1), votes));
        }
        return new(pollId, total, options);
    }
}
