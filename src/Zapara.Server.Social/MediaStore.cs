namespace Zapara.Server.Social;

public sealed class MediaStore
{
    private readonly string root;
    private readonly IObjectStore? remote;
    public MediaStore(string root, IObjectStore? remote = null)
    {
        this.root = Path.GetFullPath(root);
        this.remote = remote;
        if (remote is null) Directory.CreateDirectory(this.root);
    }

    public string Save(string storedName, byte[] bytes)
    {
        _ = PathFor(storedName);
        if (remote is not null)
        {
            remote.Put(storedName, bytes);
            return storedName;
        }
        var path = PathFor(storedName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public Stream Open(string storedName)
    {
        _ = PathFor(storedName);
        if (remote is not null)
        {
            var bytes = remote.Get(storedName) ?? throw new SocialException(404, "not_found");
            return new MemoryStream(bytes, writable: false);
        }
        var path = PathFor(storedName);
        if (!File.Exists(path)) throw new SocialException(404, "not_found");
        return File.OpenRead(path);
    }

    public void Delete(string storedName)
    {
        _ = PathFor(storedName);
        if (remote is not null)
        {
            remote.Delete(storedName);
            return;
        }
        var path = PathFor(storedName);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string storedName)
    {
        if (storedName.Length is < 8 or > 80 || storedName.Contains("..", StringComparison.Ordinal) ||
            storedName.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '-' and not '_'))
            throw new SocialException(400, "invalid_request");
        var path = Path.GetFullPath(Path.Combine(root, storedName));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new SocialException(400, "invalid_request");
        return path;
    }
}
