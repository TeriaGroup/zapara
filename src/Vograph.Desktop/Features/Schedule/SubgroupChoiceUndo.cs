namespace Vograph.Desktop.Features.Schedule;

internal sealed record SubgroupChoiceUndo(string Scope, string StreamId, string? Before, string? After, DateTimeOffset ExpiresAt)
{
    public bool Allows(string scope, string? current, DateTimeOffset now) =>
        Scope == scope && current == After && now < ExpiresAt;
}
