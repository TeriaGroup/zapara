using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace Vograph.Desktop.Features.Chat;

/// <summary>Plays voice in Windows; opens a circle in the user's video player.</summary>
internal sealed class ChatMediaPlayer : IDisposable
{
    private const string PlaybackPrefix = "zapara-chat-play-";
    private readonly string cacheDirectory;
    private MediaPlayer? player;
    private string? voicePath;
    private int generation;
    public event Action? PlaybackEnded;
    public event Action? PlaybackFailed;

    internal ChatMediaPlayer(string? cacheDirectory = null)
    {
        this.cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "ZaparaChatPlayback");
        var staleBefore = DateTimeOffset.UtcNow.AddMinutes(-10);
        SweepStaleFiles(this.cacheDirectory, staleBefore);
        if (cacheDirectory is null) SweepStaleFiles(Path.GetTempPath(), staleBefore);
    }

    public async Task PlayAsync(string kind, string? contentType, byte[] bytes, CancellationToken ct)
    {
        if (kind is not ("voice" or "circle")) throw new ArgumentException("Неизвестный вид сообщения.", nameof(kind));
        if (bytes.Length is < 12 || bytes.Length > ChatMediaLimits.MaxBytes(kind)) throw new InvalidDataException("Вложение недоступно.");
        Stop();
        var ticket = generation;
        var extension = ExtensionFor(kind, contentType, bytes);
        Directory.CreateDirectory(cacheDirectory);
        var temporary = Path.Combine(cacheDirectory, $"{PlaybackPrefix}{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            ct.ThrowIfCancellationRequested();
            if (ticket != generation) { DeleteTemporary(temporary); return; }
            var file = await StorageFile.GetFileFromPathAsync(temporary);
            ct.ThrowIfCancellationRequested();
            if (ticket != generation) { DeleteTemporary(temporary); return; }
            if (kind == "circle")
            {
                if (!await Launcher.LaunchFileAsync(file)) throw new IOException("Не найден видеоплеер.");
                _ = DeleteLaterAsync(temporary);
                return;
            }
            var media = new MediaPlayer { Source = MediaSource.CreateFromStorageFile(file) };
            media.MediaEnded += OnMediaEnded;
            media.MediaFailed += OnMediaFailed;
            player = media;
            voicePath = temporary;
            media.Play();
        }
        catch
        {
            Stop();
            DeleteTemporary(temporary);
            throw;
        }
    }

    internal static string ExtensionFor(string kind, string? contentType, ReadOnlySpan<byte> bytes)
    {
        if (kind is not ("voice" or "circle")) throw new ArgumentException("Неизвестный вид сообщения.", nameof(kind));
        if (bytes.Length < 12) throw new InvalidDataException("Вложение недоступно.");
        if (bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3
            || contentType is "audio/webm" or "video/webm") return ".webm";
        if (kind == "circle") return ".mp4";
        if (bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S'
            || contentType == "audio/ogg") return ".ogg";
        if (bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3'
            || bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0
            || contentType == "audio/mpeg") return ".mp3";
        return ".m4a";
    }

    public void Stop()
    {
        Interlocked.Increment(ref generation);
        var current = player;
        player = null;
        if (current is not null)
        {
            current.MediaEnded -= OnMediaEnded;
            current.MediaFailed -= OnMediaFailed;
            current.Dispose();
        }
        var old = voicePath;
        voicePath = null;
        if (old is not null) DeleteTemporary(old);
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        Stop();
        PlaybackEnded?.Invoke();
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        Stop();
        PlaybackFailed?.Invoke();
    }

    private static async Task DeleteLaterAsync(string path)
    {
        await Task.Delay(TimeSpan.FromMinutes(5));
        await DeleteWithRetryAsync(path, 6, TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private static void DeleteTemporary(string path)
    {
        if (!TryDelete(path)) _ = DeleteWithRetryAsync(path, 6, TimeSpan.FromSeconds(5), CancellationToken.None);
    }

    internal static async Task<bool> DeleteWithRetryAsync(string path, int attempts, TimeSpan delay, CancellationToken ct)
    {
        if (!IsOwnedPlaybackFile(path)) return false;
        if (attempts is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(attempts));
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (TryDelete(path)) return true;
            if (attempt + 1 < attempts) await Task.Delay(delay, ct);
        }
        return false;
    }

    internal static void SweepStaleFiles(string directory, DateTimeOffset cutoff)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, PlaybackPrefix + "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsOwnedPlaybackFile(path)) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(path) <= cutoff.UtcDateTime) TryDelete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsOwnedPlaybackFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension is not (".mp4" or ".webm" or ".m4a" or ".ogg" or ".mp3")) return false;
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(PlaybackPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(name[PlaybackPrefix.Length..], "N", out _);
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return !File.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose() => Stop();
}
