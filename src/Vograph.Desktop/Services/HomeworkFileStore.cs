using System.Text.Json;
using SkiaSharp;
using Vograph.Core.Services;

namespace Vograph.Desktop.Services;

public sealed record HomeworkStoredFile(string Id, string Kind, string Name, string Mime);

public sealed class HomeworkFileStore
{
    private readonly string _root;
    private readonly object _gate = new();

    public HomeworkFileStore(string root) => _root = root;

    public IReadOnlyList<HomeworkStoredFile> List(long homeworkId)
    {
        lock (_gate) return Read(ItemDir(homeworkId));
    }

    public HomeworkStoredFile Stage(string draft, string path, bool photo, int shown = -1)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new HomeworkFileException("bad");
        var limit = photo ? HomeworkFileRules.PhotoBytes : HomeworkFileRules.DocumentBytes;
        if (info.Length <= 0) throw new HomeworkFileException("bad");
        if (info.Length > limit) throw new HomeworkFileException("big");
        var bytes = File.ReadAllBytes(path);
        lock (_gate)
        {
            var kept = shown < 0 ? 0 : shown;
            return StageBytes(draft, photo ? "photo" : "document", info.Name, bytes, kept);
        }
    }

    public HomeworkStoredFile StageBytes(string draft, string kind, string rawName, byte[] bytes, int used)
    {
        lock (_gate)
        {
            var existing = Read(DraftDir(draft));
            if (used + existing.Count >= HomeworkFileRules.MaxFiles) throw new HomeworkFileException("full");
            var stored = Write(DraftDir(draft), kind, rawName, bytes);
            WriteIndex(DraftDir(draft), existing.Append(stored).ToList());
            return stored;
        }
    }

    public void Commit(string draft, long homeworkId, IEnumerable<string> drop)
    {
        lock (_gate)
        {
            var removed = drop.ToHashSet(StringComparer.Ordinal);
            var dir = ItemDir(homeworkId);
            var kept = Read(dir).Where(file => !removed.Contains(file.Id)).ToList();
            foreach (var id in removed)
            {
                var path = Inside(dir, id);
                if (path is not null && File.Exists(path)) File.Delete(path);
            }
            var moving = Read(DraftDir(draft)).Take(Math.Max(0, HomeworkFileRules.MaxFiles - kept.Count)).ToList();
            Directory.CreateDirectory(dir);
            foreach (var file in moving)
            {
                var from = Inside(DraftDir(draft), file.Id);
                var to = Inside(dir, file.Id);
                if (from is not null && to is not null && File.Exists(from)) File.Copy(from, to, true);
            }
            WriteIndex(dir, kept.Concat(moving).Take(HomeworkFileRules.MaxFiles).ToList());
            var draftDir = DraftDir(draft);
            if (Directory.Exists(draftDir)) Directory.Delete(draftDir, true);
        }
    }

    public void Discard(string draft)
    {
        lock (_gate)
        {
            var dir = DraftDir(draft);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    public void DiscardFile(string draft, string fileId)
    {
        lock (_gate)
        {
            var dir = DraftDir(draft);
            var path = Inside(dir, fileId);
            if (path is not null && File.Exists(path)) File.Delete(path);
            if (!Directory.Exists(dir)) return;
            WriteIndex(dir, Read(dir).Where(file => file.Id != fileId).ToList());
        }
    }

    public void DeleteHomework(long homeworkId)
    {
        lock (_gate)
        {
            var dir = ItemDir(homeworkId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    public string? PathOf(long homeworkId, string fileId)
    {
        lock (_gate)
        {
            var path = Inside(ItemDir(homeworkId), fileId);
            return path is not null && File.Exists(path) ? path : null;
        }
    }

    private HomeworkStoredFile Write(string dir, string kind, string rawName, byte[] bytes)
    {
        var accepted = HomeworkFileRules.Accept(kind, rawName, bytes.Length);
        byte[] payload = bytes;
        var name = accepted;
        var mime = MimeFor(HomeworkFileRules.ExtensionOf(accepted));
        if (kind == "photo")
        {
            payload = Jpeg(bytes);
            name = Path.GetFileNameWithoutExtension(accepted) + ".jpg";
            mime = "image/jpeg";
        }
        var id = Guid.NewGuid().ToString("N") + "." + HomeworkFileRules.ExtensionOf(name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, id), payload);
        return new HomeworkStoredFile(id, kind, name, mime);
    }

    private static byte[] Jpeg(byte[] bytes)
    {
        try
        {
        using var probe = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(probe) ?? throw new HomeworkFileException("bad");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 24_000_000)
            throw new HomeworkFileException("bad");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new HomeworkFileException("bad");
        var edge = Math.Max(bitmap.Width, bitmap.Height);
        using var scaled = edge <= 1600
            ? null
            : bitmap.Resize(new SKImageInfo(
                Math.Max(1, (int)(bitmap.Width * 1600L / edge)),
                Math.Max(1, (int)(bitmap.Height * 1600L / edge))), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        using var image = SKImage.FromBitmap(scaled ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 70) ?? throw new HomeworkFileException("bad");
        if (data.Size > 4 * 1024 * 1024) throw new HomeworkFileException("big");
        return data.ToArray();
        }
        catch (HomeworkFileException) { throw; }
        catch (Exception) { throw new HomeworkFileException("bad"); }
    }

    private List<HomeworkStoredFile> Read(string dir)
    {
        var index = Path.Combine(dir, "index.json");
        if (!File.Exists(index)) return new List<HomeworkStoredFile>();
        try
        {
            var files = JsonSerializer.Deserialize<List<HomeworkStoredFile>>(File.ReadAllText(index)) ?? new List<HomeworkStoredFile>();
            return files.Where(file => SafeId(file.Id) && File.Exists(Path.Combine(dir, file.Id))).ToList();
        }
        catch (JsonException) { return new List<HomeworkStoredFile>(); }
    }

    private static void WriteIndex(string dir, List<HomeworkStoredFile> files)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.json"), JsonSerializer.Serialize(files));
    }

    private string ItemDir(long homeworkId) => Path.Combine(_root, "items", homeworkId.ToString());

    private string DraftDir(string draft)
    {
        if (!SafeId(draft)) throw new HomeworkFileException("bad");
        return Path.Combine(_root, "drafts", draft);
    }

    private static bool SafeId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 90 || id.Contains("..", StringComparison.Ordinal)) return false;
        var dot = id.IndexOf('.');
        var stem = dot < 0 ? id : id[..dot];
        var ext = dot < 0 ? "" : id[(dot + 1)..];
        if (dot >= 0 && (ext.Length is < 1 or > 5 || id.IndexOf('.', dot + 1) >= 0)) return false;
        return stem.Length is >= 1 and <= 80 && stem.All(SafeChar) && ext.All(SafeChar);
        static bool SafeChar(char ch) => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_';
    }

    private static string? Inside(string dir, string id)
    {
        if (!SafeId(id)) return null;
        var root = Path.GetFullPath(dir);
        var path = Path.GetFullPath(Path.Combine(root, id));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static string MimeFor(string ext) => ext switch
    {
        "pdf" => "application/pdf",
        "txt" or "csv" => "text/plain",
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        _ => "application/octet-stream"
    };
}
