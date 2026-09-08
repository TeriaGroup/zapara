using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Zapara.Server.Accounts;

public sealed record AccountUser(Guid UserId);

public sealed class AccountPasswordWork
{
    private static readonly ConcurrencyLimiter Limiter = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = 8, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });
    private readonly IPasswordHasher<AccountUser> hasher;
    private readonly Lazy<string> dummyHash;
    private static readonly AccountUser DummyUser = new(Guid.Empty);

    public AccountPasswordWork(IPasswordHasher<AccountUser>? hasher = null)
    {
        this.hasher = hasher ?? new PasswordHasher<AccountUser>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = 100000
        }));
        // Initialized once, inside a hash permit; no database or CPU work in the constructor.
        dummyHash = new(() => this.hasher.HashPassword(DummyUser, "dummy credential not an account password"));
    }

    internal string Hash(Guid userId, string password, CancellationToken ct)
        => Work(() => hasher.HashPassword(new(userId), password), ct);

    internal PasswordVerificationResult Verify(Guid userId, string hash, string password, CancellationToken ct)
        => Work(() => hasher.VerifyHashedPassword(new(userId), hash, password), ct);

    internal void Dummy(string password, CancellationToken ct)
        => Work(() => hasher.VerifyHashedPassword(DummyUser, dummyHash.Value, password), ct);

    private static T Work<T>(Func<T> work, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var lease = Limiter.AttemptAcquire(1);
        if (!lease.IsAcquired) throw new AccountServiceException(AccountFailure.RateLimited);
        var result = work();
        // Synchronous framework work cannot be interrupted, but cancelled results are never persisted.
        ct.ThrowIfCancellationRequested();
        return result;
    }
}
