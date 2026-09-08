using System.Text;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Accounts;
using static Zapara.Server.Tests.AccountApiTestHost;

namespace Zapara.Server.Tests;

public sealed partial class AccountApiTests
{
    [Theory]
    [InlineData("revoke")]
    [InlineData("refresh")]
    [InlineData("password")]
    public async Task ACC04_ACC05_Authenticated_pending_body_cannot_mutate_after_revocation(string operation)
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register(); var actor = await host.Login(); var pending = await host.Login();
        await using var body = new GatedBody(Encoding.UTF8.GetBytes("{\"displayName\":\"must-not-be-written\"}"));
        var response = host.Factory.Server.SendAsync(context =>
        {
            context.Request.Method = "PATCH";
            context.Request.Path = "/api/v1/account/me";
            context.Request.Headers.Authorization = "Bearer " + pending.AccessToken;
            context.Request.ContentType = "application/json";
            context.Request.Body = body;
        }, Ct);
        try
        {
            // The endpoint starts reading only after the opaque handler has authenticated.
            await body.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            switch (operation)
            {
                case "revoke": await host.Send("DELETE", "/account/devices/" + pending.FamilyId, 204, bearer: actor.AccessToken); break;
                case "refresh": await host.Refresh(pending.RefreshToken); break;
                case "password": await host.Send("POST", "/account/password/change", 204,
                    new ChangePasswordRequest(Password, NewPassword), actor.AccessToken); break;
            }
        }
        finally { body.Release.TrySetResult(); }
        var result = await response;
        Assert.Equal(401, result.Response.StatusCode);
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users WHERE display_name IS NOT NULL"));
    }

    [Fact]
    public async Task Cancelled_account_request_never_returns_empty_success()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register(); var session = await host.Login();
        await using var body = new GatedBody(Encoding.UTF8.GetBytes("{\"displayName\":\"cancelled\"}"));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var request = host.Factory.Server.SendAsync(context =>
        {
            context.Request.Method = "PATCH"; context.Request.Path = "/api/v1/account/me";
            context.Request.Headers.Authorization = "Bearer " + session.AccessToken;
            context.Request.ContentType = "application/json"; context.Request.Body = body;
        }, cancel.Token);
        try
        {
            await body.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            cancel.Cancel();
            // Raw TestServer exposes the framework's aborted-request response (HttpClient may throw).
            var aborted = await request;
            Assert.Equal(499, aborted.Response.StatusCode);
        }
        finally { body.Release.TrySetResult(); }
        Assert.Equal(0, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.users WHERE display_name IS NOT NULL"));
    }

    private sealed class GatedBody(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
