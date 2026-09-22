using Microsoft.Extensions.Hosting;
using Npgsql;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal sealed class SystemBallotService(AccountsDataSource data, CommunitiesConfiguration configuration, IHostEnvironment environment) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PlantAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { }
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PlantAsync(CancellationToken ct)
    {
        await MessengerSchema.EnsureAsync(data, configuration, ct);
        await using var connection = data.CreateConnection();
        await connection.OpenAsync(ct);
        if (!await PresentAsync(connection, configuration.MessagesSchema + ".ballots", ct)) return;
        var question = CommunityValidation.Question(BallotRules.WeekQuestion);
        var options = CommunityValidation.Options(BallotRules.WeekOptions.ToArray());
        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand($"""
            SELECT community.community_id
            FROM {configuration.QuotedSchema}.communities AS community
            WHERE (
                SELECT count(*) FROM {configuration.QuotedSchema}.memberships AS member
                WHERE member.community_id=community.community_id AND member.status='active'
            ) >= @minimum
            """, connection))
        {
            command.Parameters.AddWithValue("minimum", BallotRules.MinimumGroup);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        }
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;
            try { await PlantOneAsync(connection, id, question, options, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
    }

    private async Task PlantOneAsync(NpgsqlConnection connection, Guid communityId, string question, IReadOnlyList<string> options, CancellationToken ct)
    {
        var now = Clock();
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var locked = Command(connection, tx, $"""
            SELECT community_id FROM {configuration.QuotedSchema}.communities WHERE community_id=@id FOR UPDATE
            """, ("id", communityId)))
        await using (var reader = await locked.ExecuteReaderAsync(ct))
            if (!await reader.ReadAsync(ct)) return;
        await using (var close = Command(connection, tx, $"""
            UPDATE {configuration.QuotedMessages}.ballots SET status='closed'
            WHERE community_id=@id AND status IN ('collecting','open') AND deadline_at <= @now
            """, ("id", communityId), ("now", now)))
            await close.ExecuteNonQueryAsync(ct);
        var members = await ScalarAsync(connection, tx, $"""
            SELECT count(*)::int FROM {configuration.QuotedSchema}.memberships
            WHERE community_id=@id AND status='active'
            """, ct, ("id", communityId));
        var recent = await ScalarAsync(connection, tx, $"""
            SELECT count(*)::int FROM {configuration.QuotedMessages}.ballots
            WHERE community_id=@id AND origin='system' AND created_at > @since
            """, ct, ("id", communityId), ("since", now.AddDays(-6)));
        var active = await ScalarAsync(connection, tx, $"""
            SELECT count(*)::int FROM {configuration.QuotedMessages}.ballots
            WHERE community_id=@id AND status IN ('collecting','open')
            """, ct, ("id", communityId));
        if (members < BallotRules.MinimumGroup || recent > 0 || active >= BallotRules.ActiveLimit)
        {
            await tx.CommitAsync(ct);
            return;
        }
        var ballotId = Guid.NewGuid();
        var deadline = now.AddDays(5);
        await using (var insert = Command(connection, tx, $"""
            INSERT INTO {configuration.QuotedMessages}.ballots(
                ballot_id,community_id,question,origin,status,deadline_at,opened_at,created_by,created_at)
            VALUES(@id,@community,@question,'system','open',@deadline,@opened,NULL,@created)
            """, ("id", ballotId), ("community", communityId), ("question", question), ("deadline", deadline), ("opened", now), ("created", now)))
            await insert.ExecuteNonQueryAsync(ct);
        for (var i = 0; i < options.Count; i++)
        {
            await using var option = Command(connection, tx, $"""
                INSERT INTO {configuration.QuotedMessages}.ballot_options(option_id,ballot_id,label,ordinal)
                VALUES(@id,@ballot,@label,@ordinal)
                """, ("id", Guid.NewGuid()), ("ballot", ballotId), ("label", options[i]), ("ordinal", i));
            await option.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task<bool> PresentAsync(NpgsqlConnection connection, string name, CancellationToken ct)
    {
        await using var probe = new NpgsqlCommand("SELECT to_regclass(@name)::text", connection);
        probe.Parameters.AddWithValue("name", name);
        return await probe.ExecuteScalarAsync(ct) is string text && text.Length > 0;
    }

    private static async Task<int> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, tx, sql, parameters);
        return await command.ExecuteScalarAsync(ct) switch
        {
            int value => value,
            long value => (int)value,
            _ => 0
        };
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static DateTimeOffset Clock()
    {
        var now = DateTimeOffset.UtcNow;
        return new(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }
}
