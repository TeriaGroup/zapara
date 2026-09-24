using System.Text;

namespace Zapara.Server.Operator;

public sealed class SupportAttachmentException(string message) : Exception(message);

public sealed record SupportFile(string Kind, string Name, string ContentType, byte[] Bytes);

public static class SupportFiles
{
    public const int PhotoBytes = 4 * 1024 * 1024;
    public const int LogBytes = 512 * 1024;
    public const int MaxEach = 3;

    public static void CheckCounts(IReadOnlyList<SupportFile> files)
    {
        if (files.Count(file => file.Kind == "photo") > MaxEach)
            throw new SupportAttachmentException("Можно приложить не больше трёх фотографий.");
        if (files.Count(file => file.Kind == "log") > MaxEach)
            throw new SupportAttachmentException("Можно приложить не больше трёх логов.");
    }

    public static SupportFile Inspect(string kind, string rawName, byte[] bytes)
    {
        var name = Path.GetFileName(rawName.Replace('\\', '/')).Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new SupportAttachmentException(kind == "photo" ? "Нужна фотография JPEG, PNG или WebP." : "Лог должен быть текстовым файлом .txt или .log.");
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (kind == "photo")
        {
            var type = ext switch
            {
                ".jpg" or ".jpeg" when Jpeg(bytes) => "image/jpeg",
                ".png" when Png(bytes) => "image/png",
                ".webp" when Webp(bytes) => "image/webp",
                _ => null
            };
            if (type is null) throw new SupportAttachmentException("Нужна фотография JPEG, PNG или WebP.");
            return new SupportFile("photo", name, type, bytes);
        }
        if (kind != "log" || ext is not (".txt" or ".log") || !Text(bytes))
            throw new SupportAttachmentException("Лог должен быть текстовым файлом .txt или .log.");
        return new SupportFile("log", name, "text/plain", bytes);
    }

    private static bool Jpeg(byte[] bytes) => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private static bool Png(byte[] bytes) => bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static bool Webp(byte[] bytes) => bytes.Length > 12
        && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
        && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';

    private static bool Text(byte[] bytes)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return !text.Contains('\0');
        }
        catch (DecoderFallbackException) { return false; }
    }
}
