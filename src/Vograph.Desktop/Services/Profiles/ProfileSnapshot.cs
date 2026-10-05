using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Shell;

namespace Vograph.Desktop.Services.Profiles;

public enum ProfilePhase { Idle, Preparing, Draining, Committing, RecoveryRequired, Closed }
public enum ProfileFailure { Cancelled, Busy, Preparation, Credentials, Publication }
public sealed record ProfileSnapshot(ProfileDescriptor Profile, AccountSessionIdentity? Identity,
    bool ReauthRequired, ProfilePhase Phase, ProfileFailure? Failure = null, AccountClientFailure? AccountFailure = null);
public sealed record ProfileSwitchResult(bool Committed, ProfileSnapshot Snapshot);
public sealed record ProfileRoot(AppServices Services, ShellViewModel Shell);
