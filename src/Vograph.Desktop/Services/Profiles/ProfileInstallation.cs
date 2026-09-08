using System.Text;

namespace Vograph.Desktop.Services.Profiles;

public static class ProfileInstallation
{
    public static Guid LoadOrCreate(string existingRoot)
    {
        if (!Path.IsPathFullyQualified(existingRoot) || !Directory.Exists(existingRoot))
            throw new ArgumentException("Требуется существующий каталог установки.");
        var path = Path.Combine(existingRoot, "installation.id");
        if (!File.Exists(path))
        {
            var id = Guid.NewGuid();
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            file.Write(Encoding.ASCII.GetBytes(id.ToString("D")));
            file.Flush(true);
            return id;
        }
        if (!Guid.TryParseExact(File.ReadAllText(path), "D", out var saved) || saved == Guid.Empty)
            throw new InvalidDataException("Повреждён идентификатор установки.");
        return saved;
    }
}
