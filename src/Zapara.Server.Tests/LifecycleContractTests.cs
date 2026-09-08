using System.Text.Json;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;

namespace Zapara.Server.Tests;

public sealed class LifecycleContractTests
{
    private static readonly JsonSerializerOptions Json = AccountJson.CreateOptions();

    [Fact]
    public void Export_and_delete_requests_redact_proof_tokens()
    {
        const string secret = "SECRET_canary_proof_token_value_aaaaaaa";
        var proof = new ProofRequest(secret);
        Assert.Contains("REDACTED", proof.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, proof.ToString(), StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProofRequest>(
            "{\"proofToken\":\"x\",\"extra\":1}", Json));
    }

    [Fact]
    public void Delete_response_does_not_claim_remote_wipe()
    {
        var response = new DeleteAccountResponse("deleting", false);
        Assert.Equal("deleting", response.Status);
        Assert.False(response.RemoteWipe);
        var json = JsonSerializer.Serialize(response, Json);
        Assert.Contains("\"remoteWipe\":false", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"remoteWipe\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_job_round_trips_camel_case_and_rejects_unknown_members()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var created = DateTimeOffset.Parse("2026-09-08T12:00:00Z");
        var job = new ExportJobResponse(id, "ready", created, created, created.AddDays(1));
        var json = JsonSerializer.Serialize(job, Json);
        var back = JsonSerializer.Deserialize<ExportJobResponse>(json, Json)!;
        Assert.Equal(id, back.ExportId);
        Assert.Equal("ready", back.Status);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExportJobResponse>(
            "{\"exportId\":\"11111111-1111-1111-1111-111111111111\",\"status\":\"ready\",\"createdAt\":\"2026-09-08T12:00:00+00:00\",\"extra\":1}", Json));
    }
}
