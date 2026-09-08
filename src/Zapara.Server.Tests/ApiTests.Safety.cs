using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.ApiTestFactory;

namespace Zapara.Server.Tests;

public sealed partial class ApiTests
{
    [Fact]
    public async Task TT011_Ordinal_order_opaque_ids_and_raw_fields()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var seed = TestSnapshotFactory.Create(db.Clock);
        var raw = seed.Lessons[0].Value with
        {
            ClassroomRaw = "  493*; дистанционно  ", RoomRaw = "493*", BuildingRaw = "УЛК",
            TeacherRaw = "  Иванов И.И.; Петров П.П.  ", SubjectRaw = "лек  Математика ", SubjectNormalized = "Математика"
        };
        var lessons = new[]
        {
            raw with { DayOfWeek = 7, Parity = 0, Index = 1 }, raw with { DayOfWeek = 1, Parity = 2, Index = 1 },
            raw with { DayOfWeek = 1, Parity = 1, Index = 2 }, raw with { DayOfWeek = 1, Parity = 1, Index = 1 }
        };
        var groups = new[] { new GroupDto("z", "А", 0), new GroupDto("opaque-001", "Z", 4), new GroupDto("a", "А", 0) };
        await PublishAsync(db, new ValidatedSnapshot(seed.Period, groups,
            lessons.Select(l => new SnapshotLesson("opaque-001", l)), seed.Source));
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();
        var response = await GetAsync(client, "/api/v1/groups");
        Assert.Equal(new[] { "opaque-001", "a", "z" }, response.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
        var schedule = (await GetAsync(client, "/api/v1/groups/opaque-001/timetable"))
            .Deserialize<TimetableResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(lessons.OrderBy(l => l.DayOfWeek).ThenBy(l => l.Parity).ThenBy(l => l.Index), schedule.Lessons);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task TT013_Unexpected_failure_is_redacted_in_development()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await PublishAsync(db);
        await using var factory = Create(db.Configuration, new ThrowingClock());
        using var client = factory.CreateClient();
        await ProblemAsync(client, "/api/v1/status?private-token=synthetic", HttpStatusCode.InternalServerError, "internal_error");
        Assert.Equal("{\"status\":\"live\"}", await client.GetStringAsync("/health/live", Ct));
    }

    [Fact]
    public async Task TT011_Cancelled_database_read_does_not_succeed()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await PublishAsync(db);
        await using var factory = Create(db.Configuration, db.Clock);
        using var client = factory.CreateClient();
        await using var connection = db.Configuration.CreateDedicatedConnection();
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var command = new NpgsqlCommand($"LOCK TABLE {db.QuotedSchema}.state IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await command.ExecuteNonQueryAsync(Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var request = client.GetAsync("/api/v1/groups", cancellation.Token);
        try
        {
            var waiting = false;
            for (var i = 0; i < 100 && !waiting; i++)
            {
                waiting = await db.ScalarAsync<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%{db.Schema}%' AND pid <> pg_backend_pid())", Ct);
                if (!waiting) await Task.Delay(20, Ct);
            }
            Assert.True(waiting, "The HTTP request must actually be waiting on this test's PostgreSQL table lock.");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        }
        finally
        {
            cancellation.Cancel();
            await transaction.RollbackAsync(CancellationToken.None);
            try { using var response = await request; } catch (OperationCanceledException) { }
        }
        await GetAsync(client, "/api/v1/groups");
        await db.ReceiptAsync(Ct);
    }

    private sealed class ThrowingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("synthetic secret SQL password path");
    }
}
