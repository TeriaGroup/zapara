namespace Vograph.Core.Services;

/// <summary>Local choice of the public release repo or the private alpha repo. The token stays in this file and is not synced.</summary>
public static class UpdateChannelStore
{
    public const string Owner = "TeriaGroup";
    public const string PublicRepo = "zapara-releases";
    public const string AlphaRepo = "zapara";

    public static string FilePath { get; set; } = Path.Combine(VographDataRoot.DefaultDir, "update-source.txt");

    public readonly record struct Choice(bool Alpha, string Token)
    {
        public string Repo => Alpha ? AlphaRepo : PublicRepo;
        public string Page => $"https://github.com/{Owner}/{Repo}/releases";
    }

    public static Choice Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Choice(false, "");
            var lines = File.ReadAllLines(FilePath);
            var alpha = lines.Length > 0 && lines[0].Trim() == "alpha";
            var token = lines.Length > 1 ? lines[1].Trim() : "";
            return new Choice(alpha, token);
        }
        catch
        {
            return new Choice(false, "");
        }
    }

    public static void Write(bool alpha, string? token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var body = (alpha ? "alpha" : "release") + "\n" + (token ?? "").Trim();
        File.WriteAllText(FilePath, body);
    }
}
