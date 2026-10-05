using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

public sealed class AvatarAccessTests
{
    [Fact]
    public async Task Invalid_session_is_rejected_before_decoding_or_uploading()
    {
        var service = new AvatarService(new RejectedAccount(), null!, null!, null!, null!);
        var error = await Assert.ThrowsAsync<AccountServiceException>(() =>
            service.PutAsync("expired", null, [], TestContext.Current.CancellationToken));
        Assert.Equal(AccountFailure.InvalidSession, error.Failure);
    }

    [Fact]
    public async Task Group_gate_denies_upload_delete_and_read_before_storage()
    {
        var access = new DeniedGroup();
        var service = new AvatarService(new CallbackAccount(), null!, null!, null!, null!, access);
        var group = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var upload = await Assert.ThrowsAsync<SocialException>(() => service.PutAsync("account", group, [], ct));
        Assert.Equal(403, upload.Status);
        Assert.True(access.Write);
        var delete = await Assert.ThrowsAsync<SocialException>(() => service.DeleteAsync("account", group, ct));
        Assert.Equal(403, delete.Status);
        Assert.True(access.Write);
        var read = await Assert.ThrowsAsync<SocialException>(() => service.OpenGroupAsync("account", group, ct));
        Assert.Equal(404, read.Status);
        Assert.False(access.Write);
    }

    [Fact]
    public async Task Disabled_community_module_cannot_be_bypassed_by_upload()
    {
        var service = new AvatarService(new CallbackAccount(), null!, null!, null!, null!);
        var error = await Assert.ThrowsAsync<SocialException>(() =>
            service.PutAsync("account", Guid.NewGuid(), [], TestContext.Current.CancellationToken));
        Assert.Equal(404, error.Status);
    }

    private sealed class RejectedAccount : IAccountUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(string token, Func<TrustedAccountContext, CancellationToken, Task<T>> operation, CancellationToken ct = default)
            => throw new AccountServiceException(AccountFailure.InvalidSession);
    }

    private sealed class CallbackAccount : IAccountUnitOfWork
    {
        // These denial paths must stop at the access gate, before touching a transaction.
        public Task<T> ExecuteAsync<T>(string token, Func<TrustedAccountContext, CancellationToken, Task<T>> operation, CancellationToken ct = default)
            => operation(null!, ct);
    }

    private sealed class DeniedGroup : IAvatarCommunityAccess
    {
        public bool Write { get; private set; }
        public Task<string> RequireAsync(TrustedAccountContext context, Guid communityId, bool write, CancellationToken ct)
        {
            Write = write;
            throw new SocialException(write ? 403 : 404, write ? "forbidden" : "not_found");
        }
        public Task<bool> ShareMembershipAsync(TrustedAccountContext context, Guid userId, CancellationToken ct)
            => Task.FromResult(false);
    }
}
