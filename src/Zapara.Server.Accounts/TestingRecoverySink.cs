using System.Collections.Concurrent;

namespace Zapara.Server.Accounts;

public sealed class TestingRecoverySink : IRecoveryDelivery
{
    private readonly ConcurrentQueue<RecoveryDeliveryMessage> messages = new();

    public IReadOnlyList<RecoveryDeliveryMessage> Messages => messages.ToArray();

    public Task SendVerificationAsync(Guid userId, string email, string token, CancellationToken ct)
    {
        messages.Enqueue(new("verify", userId, email, token));
        return Task.CompletedTask;
    }

    public Task SendResetAsync(string email, string token, CancellationToken ct)
    {
        messages.Enqueue(new("reset", null, email, token));
        return Task.CompletedTask;
    }

    public RecoveryDeliveryMessage Last(string kind) =>
        Messages.LastOrDefault(message => message.Kind == kind)
        ?? throw new InvalidOperationException("Сообщение восстановления не найдено.");

    public override string ToString() => "TestingRecoverySink { [REDACTED] }";
}

public sealed record RecoveryDeliveryMessage(string Kind, Guid? UserId, string Email, string Token)
{
    public override string ToString() => "RecoveryDeliveryMessage { [REDACTED] }";
}
