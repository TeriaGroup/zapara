namespace Vograph.Desktop.Services.Profiles;

public sealed record ProfileDescriptor
{
    private ProfileDescriptor(string root, string? serverKey, Guid? userId)
    {
        GlobalDataDir = Path.GetFullPath(root);
        ServerKey = serverKey;
        UserId = userId;
        DatabasePath = userId is null ? Path.Combine(GlobalDataDir, "vograph.db")
            : Path.Combine(GlobalDataDir, "profiles", serverKey!, userId.Value.ToString("D"), "vograph.db");
    }
    public string GlobalDataDir { get; }
    public string DatabasePath { get; }
    public string? ServerKey { get; }
    public Guid? UserId { get; }
    public bool IsGuest => UserId is null;
    public static ProfileDescriptor Guest(string root) => new(root, null, null);
    public static ProfileDescriptor Account(string root, string key, Guid userId)
    {
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c)) || key != key.ToUpperInvariant() || userId == Guid.Empty)
            throw new ArgumentException("Недопустимый идентификатор профиля.");
        return new(root, key, userId);
    }
}
