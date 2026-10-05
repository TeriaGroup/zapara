using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Vograph.Core.Services.Social;

namespace Vograph.Desktop.Features.Chat;

/// <summary>Memory-only, bounded WebP cache owned by one profile and one server.</summary>
public sealed class AvatarImages(AvatarHttpClient client) : IDisposable
{
    public event Action? AuthorizationFailed;
    private readonly object gate = new();
    private readonly Dictionary<(bool Group, Guid Id), (byte[]? Bytes, DateTimeOffset Checked)> cache = [];
    private readonly Dictionary<(bool Group, Guid Id), long> pending = [];
    private long generation;
    private long serial;
    private bool disposed;

    public Task<Bitmap?> UserAsync(string access, Guid userId, CancellationToken ct = default)
        => LoadAsync(false, userId, access, ct);
    public Task<Bitmap?> GroupAsync(string access, Guid communityId, CancellationToken ct = default)
        => LoadAsync(true, communityId, access, ct);

    public async Task PutMeAsync(string access, Guid userId, string name, byte[] bytes, CancellationToken ct = default)
    {
        await client.PutMeAsync(access, name, bytes, ct);
        InvalidateUser(userId);
    }
    public async Task DeleteMeAsync(string access, Guid userId, CancellationToken ct = default)
    {
        await client.DeleteMeAsync(access, ct);
        InvalidateUser(userId);
    }
    public async Task PutGroupAsync(string access, Guid communityId, string name, byte[] bytes, CancellationToken ct = default)
    {
        await client.PutGroupAsync(access, communityId, name, bytes, ct);
        InvalidateGroup(communityId);
    }
    public async Task DeleteGroupAsync(string access, Guid communityId, CancellationToken ct = default)
    {
        await client.DeleteGroupAsync(access, communityId, ct);
        InvalidateGroup(communityId);
    }

    private async Task<Bitmap?> LoadAsync(bool group, Guid id, string access, CancellationToken ct)
    {
        if (id == Guid.Empty) return null;
        var key = (group, id);
        byte[]? copy = null;
        long epoch;
        long ticket = 0;
        lock (gate)
        {
            if (disposed) return null;
            epoch = generation;
            if (cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.Checked < TimeSpan.FromSeconds(90))
            {
                if (hit.Bytes is null) return null;
                copy = hit.Bytes.ToArray();
            }
            else pending[key] = ticket = ++serial;
        }
        if (copy is null)
        {
            byte[]? received = null;
            try
            {
                received = group ? await client.GroupAsync(access, id, ct) : await client.UserAsync(access, id, ct);
                lock (gate)
                {
                    if (disposed || epoch != generation || !pending.TryGetValue(key, out var latest) || latest != ticket)
                        return null;
                    Replace(key, received);
                    received = null; // cache now owns the bytes
                    copy = cache[key].Bytes?.ToArray();
                }
            }
            catch (AvatarClientException ex) when (ex.Status is 401 or 403)
            {
                Clear();
                AuthorizationFailed?.Invoke();
                throw;
            }
            finally
            {
                if (received is not null) CryptographicOperations.ZeroMemory(received);
                lock (gate) if (pending.TryGetValue(key, out var latest) && latest == ticket) pending.Remove(key);
            }
        }
        if (copy is null) return null;
        try
        {
            var image = DecodeThumbnail(copy);
            lock (gate)
            {
                if (!disposed && epoch == generation) return image;
            }
            image.Dispose();
            return null;
        }
        catch
        {
            Invalidate(key);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    private static Bitmap DecodeThumbnail(byte[] bytes)
    {
        using var probe = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(probe) ?? throw new InvalidDataException("Фото недоступно.");
        if (codec.Info.Width is < 1 or > 512 || codec.Info.Height != codec.Info.Width)
            throw new InvalidDataException("Фото недоступно.");
        using var stream = new MemoryStream(bytes, writable: false);
        return Bitmap.DecodeToWidth(stream, 128, BitmapInterpolationMode.MediumQuality);
    }

    private void Replace((bool Group, Guid Id) key, byte[]? bytes)
    {
        if (cache.Remove(key, out var old) && old.Bytes is not null) CryptographicOperations.ZeroMemory(old.Bytes);
        cache[key] = (bytes, DateTimeOffset.UtcNow);
        if (cache.Count <= 64) return;
        var oldest = cache.OrderBy(item => item.Value.Checked).First().Key;
        if (cache.Remove(oldest, out var evicted) && evicted.Bytes is not null)
            CryptographicOperations.ZeroMemory(evicted.Bytes);
    }

    public void InvalidateUser(Guid id) => Invalidate((false, id));
    public void InvalidateGroup(Guid id) => Invalidate((true, id));
    private void Invalidate((bool Group, Guid Id) key)
    {
        lock (gate)
        {
            generation++;
            pending.Remove(key);
            if (cache.Remove(key, out var entry) && entry.Bytes is not null)
                CryptographicOperations.ZeroMemory(entry.Bytes);
        }
    }
    public void Clear()
    {
        lock (gate)
        {
            generation++;
            foreach (var entry in cache.Values)
                if (entry.Bytes is not null) CryptographicOperations.ZeroMemory(entry.Bytes);
            cache.Clear();
            pending.Clear();
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        Clear();
        client.Dispose();
    }

    /// <summary>Call after every binding referencing this view-owned bitmap was cleared.</summary>
    public static void Retire(Bitmap? image)
    {
        if (image is not null) Dispatcher.UIThread.Post(image.Dispose, DispatcherPriority.Background);
    }
}
