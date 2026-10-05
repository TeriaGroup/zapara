using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace Zapara.Server.Tests;

[CollectionDefinition("Account runtime", DisableParallelization = true)]
public sealed class AccountRuntimeCollection;

internal sealed class AccountControlledHasher : IPasswordHasher<Zapara.Server.Accounts.AccountUser>, IDisposable
{
    private readonly PasswordHasher<Zapara.Server.Accounts.AccountUser> official = new(Options.Create(
        new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = 100000 }));
    internal Action? BeforeHash { get; set; }
    internal Action? AfterVerify { get; set; }
    internal int HashCalls;
    internal int VerifyCalls;
    internal ManualResetEventSlim Release { get; } = new(false);
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string HashPassword(Zapara.Server.Accounts.AccountUser user, string password)
    {
        Interlocked.Increment(ref HashCalls);
        BeforeHash?.Invoke();
        return official.HashPassword(user, password);
    }
    public PasswordVerificationResult VerifyHashedPassword(Zapara.Server.Accounts.AccountUser user, string hash, string password)
    {
        Interlocked.Increment(ref VerifyCalls);
        var result = official.VerifyHashedPassword(user, hash, password);
        AfterVerify?.Invoke();
        return result;
    }
    internal void Block()
    {
        Entered.TrySetResult();
        if (!Release.Wait(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken))
            throw new TimeoutException("Test hasher barrier timeout.");
    }
    public void Dispose() { Release.Set(); Release.Dispose(); }
}
