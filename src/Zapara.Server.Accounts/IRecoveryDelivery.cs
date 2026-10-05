namespace Zapara.Server.Accounts;

public interface IRecoveryDelivery
{
    Task SendVerificationAsync(Guid userId, string email, string token, CancellationToken ct);
    Task SendResetAsync(string email, string token, CancellationToken ct);
}

public sealed class UnconfiguredRecoveryDelivery : IRecoveryDelivery
{
    public static readonly UnconfiguredRecoveryDelivery Instance = new();
    public Task SendVerificationAsync(Guid userId, string email, string token, CancellationToken ct) => Task.CompletedTask;
    public Task SendResetAsync(string email, string token, CancellationToken ct) => Task.CompletedTask;
}
