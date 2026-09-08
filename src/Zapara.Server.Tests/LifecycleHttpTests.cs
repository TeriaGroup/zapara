using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Server.Accounts;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed class LifecycleHttpTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Export_requires_recent_proof_creates_owner_job_and_download_is_authenticated()
    {
        await using var harness = await LifecycleHarness.CreateAsync(output.WriteLine);
        await using var host = new LifecycleApiHost(harness);
        await host.Register();
        var session = await host.Login();
        await host.Send("POST", "/account/exports", 401, new ProofRequest(new string('A', 43)), code: "invalid_session");
        await host.Send("POST", "/account/exports", 400, new { }, session.AccessToken, code: "invalid_request");
        await host.Send("POST", "/account/exports", 403, new ProofRequest(new string('A', 43)), session.AccessToken, code: "invalid_external_proof");
        var recoveryProof = await host.Proof(session, "set_recovery_email");
        await host.Send("POST", "/account/exports", 403, new ProofRequest(recoveryProof), session.AccessToken, code: "invalid_external_proof");
        var proof = await host.Proof(session, "export");
        var created = await host.Send("POST", "/account/exports", 202, new ProofRequest(proof), session.AccessToken);
        var exportId = created.GetProperty("exportId").GetGuid();
        Assert.Equal("ready", created.GetProperty("status").GetString());
        Assert.Equal(exportId.ToString("D"), created.GetProperty("exportId").GetString());
        var status = await host.Send("GET", "/account/exports/" + exportId.ToString("D"), 200, bearer: session.AccessToken);
        Assert.Equal("ready", status.GetProperty("status").GetString());
        await host.Send("GET", "/account/exports/" + Guid.NewGuid().ToString("D"), 404, bearer: session.AccessToken, code: "export_not_found");
        var (payload, fileName, media) = await host.Download("/account/exports/" + exportId.ToString("D") + "/download", 200, session.AccessToken);
        Assert.Equal("application/json", media);
        Assert.Contains(exportId.ToString("D"), fileName, StringComparison.Ordinal);
        Assert.Equal(session.User.UserId, payload.GetProperty("profile").GetProperty("userId").GetGuid());
        Assert.Equal("synthetic", payload.GetProperty("profile").GetProperty("username").GetString());
        Assert.True(payload.GetProperty("devices").GetArrayLength() >= 1);
        var device = payload.GetProperty("devices")[0];
        Assert.False(device.TryGetProperty("accessToken", out _));
        Assert.False(device.TryGetProperty("refreshToken", out _));
        Assert.False(payload.TryGetProperty("passwordHash", out _));
        var raw = payload.GetRawText();
        Assert.DoesNotContain(session.AccessToken, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(session.RefreshToken, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("za_", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("zr_", raw, StringComparison.Ordinal);
        await host.Send("GET", "/account/exports/" + exportId.ToString("D") + "/download", 401, code: "invalid_session");
        using var publicUrl = await host.Client.GetAsync("/api/v1/account/exports/" + exportId.ToString("D") + "/download", Ct);
        Assert.Equal(401, (int)publicUrl.StatusCode);
        var logs = string.Join('\n', host.Logs);
        foreach (var secret in new[] { Password, session.AccessToken, session.RefreshToken, proof,
                     Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")! })
            Assert.True(!logs.Contains(secret, StringComparison.Ordinal), "Export log canary must be absent.");
    }

    [Fact]
    public async Task Export_download_is_owner_only_and_foreign_status_is_404()
    {
        await using var harness = await LifecycleHarness.CreateAsync(output.WriteLine);
        await using var host = new LifecycleApiHost(harness);
        await host.Register();
        var owner = await host.Login();
        await host.Register("foreign");
        var foreign = await host.Login("foreign");
        var proof = await host.Proof(owner, "export");
        var created = await host.Send("POST", "/account/exports", 202, new ProofRequest(proof), owner.AccessToken);
        var exportId = created.GetProperty("exportId").GetGuid().ToString("D");
        await host.Send("GET", "/account/exports/" + exportId, 404, bearer: foreign.AccessToken, code: "export_not_found");
        await host.Send("GET", "/account/exports/" + exportId + "/download", 404, bearer: foreign.AccessToken, code: "export_not_found");
        await host.Send("GET", "/account/exports/not-a-guid", 400, bearer: owner.AccessToken, code: "invalid_request");
        await host.Send("GET", "/account/exports/" + Guid.NewGuid().ToString("N"), 400, bearer: owner.AccessToken, code: "invalid_request");
    }

    [Fact]
    public async Task Delete_returns_202_marks_deleting_revokes_families_and_does_not_claim_remote_wipe()
    {
        await using var harness = await LifecycleHarness.CreateAsync(output.WriteLine);
        await using var host = new LifecycleApiHost(harness);
        await host.Register();
        var first = await host.Login();
        var second = await host.Login();
        await host.Send("DELETE", "/account", 401, new ProofRequest(new string('A', 43)), code: "invalid_session");
        await host.Send("DELETE", "/account", 400, new { }, first.AccessToken, code: "invalid_request");
        await host.Send("DELETE", "/account", 403, new ProofRequest(new string('A', 43)), first.AccessToken, code: "invalid_external_proof");
        var proof = await host.Proof(first, "delete_account");
        var accepted = await host.Send("DELETE", "/account", 202, new ProofRequest(proof), first.AccessToken);
        Assert.Equal("deleting", accepted.GetProperty("status").GetString());
        Assert.False(accepted.GetProperty("remoteWipe").GetBoolean());
        Assert.Equal("deleting", await harness.Accounts.ScalarAsync<string>($"SELECT status FROM {harness.Accounts.QuotedSchema}.users"));
        Assert.Equal(2, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {harness.Accounts.QuotedSchema}.session_families WHERE revoked_at IS NOT NULL AND revocation_reason='deleting'"));
        Assert.Equal(1, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {harness.Accounts.QuotedSchema}.deletion_jobs"));
        Assert.Equal(1, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {harness.Accounts.QuotedSchema}.deletion_manifests"));
        Assert.Equal(0, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {harness.Accounts.QuotedSchema}.password_credentials"));
        foreach (var session in new[] { first, second })
        {
            await host.Send("GET", "/account/me", 401, bearer: session.AccessToken, code: "invalid_session");
            await host.Send("POST", "/auth/refresh", 401, new { refreshToken = session.RefreshToken }, code: "invalid_session");
        }
        await host.Send("POST", "/auth/login", 401, LoginBody(), code: "invalid_credentials");
        var logs = string.Join('\n', host.Logs);
        foreach (var secret in new[] { Password, first.AccessToken, proof,
                     Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES")! })
            Assert.True(!logs.Contains(secret, StringComparison.Ordinal), "Deletion log canary must be absent.");
    }

    [Fact]
    public async Task Deletion_job_is_idempotent_and_interrupted_work_resumes()
    {
        await using var harness = await LifecycleHarness.CreateAsync(output.WriteLine);
        await using var host = new LifecycleApiHost(harness);
        await host.Register("resume.user");
        var session = await host.Login("resume.user");
        var userId = session.User.UserId;
        var s = harness.Accounts.QuotedSchema;
        await harness.Accounts.ExecuteAsync($"UPDATE {s}.users SET status='deleting' WHERE user_id='{userId}'");
        await harness.Accounts.ExecuteAsync($"UPDATE {s}.session_families SET revoked_at=now(),revocation_reason='deleting' WHERE user_id='{userId}'");
        var jobId = Guid.NewGuid();
        await harness.Accounts.ExecuteAsync($"INSERT INTO {s}.deletion_jobs(job_id,user_id,status,created_at) VALUES ('{jobId}','{userId}','queued',now())");
        var lifecycle = host.Factory.Services.GetRequiredService<AccountLifecycleService>();
        await lifecycle.ProcessPendingDeletionsAsync(Ct);
        Assert.Equal("completed", await harness.Accounts.ScalarAsync<string>($"SELECT status FROM {s}.deletion_jobs WHERE job_id='{jobId}'"));
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.deletion_manifests WHERE user_id='{userId}'"));
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.password_credentials WHERE user_id='{userId}'"));
        await lifecycle.ProcessPendingDeletionsAsync(Ct);
        Assert.Equal(1L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.deletion_jobs"));
        await harness.Accounts.ExecuteAsync($"UPDATE {s}.users SET status='active',display_name='Restored' WHERE user_id='{userId}'");
        await harness.Accounts.ExecuteAsync($"INSERT INTO {s}.password_credentials(user_id,password_hash,changed_at) VALUES ('{userId}','restored-hash',now())");
        await lifecycle.ReplayDeletionManifestsAsync(Ct);
        Assert.Equal("deleting", await harness.Accounts.ScalarAsync<string>($"SELECT status FROM {s}.users WHERE user_id='{userId}'"));
        Assert.Equal(0L, await harness.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {s}.password_credentials WHERE user_id='{userId}'"));
        Assert.True(await harness.Accounts.ScalarAsync<object?>($"SELECT display_name FROM {s}.users WHERE user_id='{userId}'") is null or DBNull);
    }
}
