using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Zapara.Server.Social;

public static class PhotoCompressor
{
    public const int MaxEdge = 1920;
    public const int MaxPixels = 24_000_000;
    public const int Quality = 68;
    public const int MaxInputBytes = 25 * 1024 * 1024;

    public static (byte[] Bytes, int Width, int Height) Compress(ReadOnlySpan<byte> input)
    {
        if (input.Length < 12) throw new SocialException(400, "invalid_image");
        if (input.Length > MaxInputBytes) throw new SocialException(413, "payload_too_large");
        try
        {
            var info = Image.Identify(input);
            if (info is null || info.Width < 1 || info.Height < 1) throw new SocialException(400, "invalid_image");
            if ((long)info.Width * info.Height > MaxPixels) throw new SocialException(413, "payload_too_large");
            using var image = Image.Load(input);
            image.Mutate(operation => operation.AutoOrient());
            if (image.Width > MaxEdge || image.Height > MaxEdge)
                image.Mutate(operation => operation.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxEdge, MaxEdge),
                    Sampler = KnownResamplers.Lanczos3
                }));
            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder
            {
                Quality = Quality,
                Method = WebpEncodingMethod.Level6,
                FileFormat = WebpFileFormatType.Lossy,
                EntropyPasses = 4,
                SkipMetadata = true,
                NearLossless = false
            });
            if (output.Length is < 12 or > 8 * 1024 * 1024) throw new SocialException(400, "invalid_image");
            return (output.ToArray(), image.Width, image.Height);
        }
        catch (SocialException) { throw; }
        catch (Exception exception) when (exception is ImageFormatException or UnknownImageFormatException or InvalidImageContentException or ArgumentException or NotSupportedException)
        { throw new SocialException(400, "invalid_image"); }
    }
}

public static class DocumentPolicy
{
    public const long MaxBytes = 20L * 1024 * 1024;
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".csv", ".rtf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".zip"
    };

    public static string CleanName(string? name, long length)
    {
        if (length <= 0) throw new SocialException(400, "invalid_request");
        if (length > MaxBytes) throw new SocialException(413, "payload_too_large");
        var file = Path.GetFileName(name ?? "");
        file = string.Concat(file.Where(ch => !char.IsControl(ch) && ch is not '\\' and not '/' and not ':' and not '"' and not '<' and not '>' and not '|' and not '?' and not '*')).Trim().Trim('.');
        if (file.Length is 0 or > 120) throw new SocialException(400, "invalid_request");
        if (file.Count(ch => ch == '.') != 1) throw new SocialException(400, "invalid_request");
        if (!Allowed.Contains(Path.GetExtension(file))) throw new SocialException(400, "invalid_request");
        return file;
    }

    public static string ContentType(string cleanName) => Path.GetExtension(cleanName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".txt" => "text/plain; charset=utf-8",
        ".csv" => "text/csv; charset=utf-8",
        ".rtf" => "application/rtf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".odt" => "application/vnd.oasis.opendocument.text",
        ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
        ".odp" => "application/vnd.oasis.opendocument.presentation",
        ".zip" => "application/zip",
        _ => throw new SocialException(400, "invalid_request")
    };

    public static string Header(string name, bool inline)
    {
        var ascii = new string(name.Select(ch => ch is >= (char)32 and <= (char)126 && ch is not '"' and not '\\' ? ch : '_').ToArray());
        if (ascii.Length == 0 || ascii.Any(char.IsControl)) ascii = inline ? "photo.webp" : "file";
        return (inline ? "inline" : "attachment") + "; filename=\"" + ascii + "\"; filename*=UTF-8''" + Uri.EscapeDataString(name);
    }
}

public sealed class SocialException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
