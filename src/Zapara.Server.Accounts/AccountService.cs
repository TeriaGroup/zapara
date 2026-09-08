using Npgsql;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed partial class AccountService
{
    private readonly AccountsDataSource dataSource;
    private readonly string schema;
    private readonly TimeProvider clock;
    private readonly AccountPasswordWork passwords;

    public AccountService(AccountsDataSource dataSource, AccountsConfiguration configuration,
        TimeProvider clock, AccountPasswordWork? passwords = null)
    {
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        schema = (configuration ?? throw new ArgumentNullException(nameof(configuration))).QuotedSchema;
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.passwords = passwords ?? new AccountPasswordWork();
    }

    private async Task<T> DatabaseAsync<T>(Func<AccountRepository, Task<T>> operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var connection = dataSource.CreateConnection();
            await connection.OpenAsync(ct);
            return await operation(new(connection, schema, clock, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            throw new AccountServiceException(AccountFailure.DbUnavailable);
        }
    }

    private static T Validate<T>(Func<T> validate)
    {
        try { return validate(); }
        catch (ArgumentException) { throw new AccountServiceException(AccountFailure.InvalidRequest); }
    }

    private static void Required(object? request)
    {
        if (request is null) throw new AccountServiceException(AccountFailure.InvalidRequest);
    }
}

public sealed class AccountAuthentication
{
    internal AccountAuthentication(UserResponse user, Guid familyId, DateTimeOffset authenticatedAt, long credentialVersion)
        => (User, FamilyId, AuthenticatedAt, CredentialVersion) = (user, familyId, authenticatedAt, credentialVersion);
    public UserResponse User { get; }
    public Guid FamilyId { get; }
    public DateTimeOffset AuthenticatedAt { get; }
    public long CredentialVersion { get; }
}
