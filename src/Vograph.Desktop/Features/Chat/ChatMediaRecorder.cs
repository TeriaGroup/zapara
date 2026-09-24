using System.Diagnostics;
using System.Security.Cryptography;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Vograph.Desktop.Features.Chat;

public sealed record ChatCapturedMedia(string Kind, string FileName, byte[] Bytes, int DurationMs);

public interface IChatMediaRecorder
{
    long CurrentLength { get; }
    Task StartAsync(string kind, CancellationToken ct);
    Task<ChatCapturedMedia> FinishAsync(CancellationToken ct);
    Task CancelAsync();
}

internal static class ChatMediaLimits
{
    public static int MaxBytes(string kind) => kind switch
    {
        "voice" => 2 * 1024 * 1024,
        "circle" => 8 * 1024 * 1024,
        _ => throw new ArgumentException("Неизвестный вид записи.", nameof(kind))
    };

    public static int MaxDurationMs(string kind) => kind switch
    {
        "voice" => 180_000,
        "circle" => 60_000,
        _ => throw new ArgumentException("Неизвестный вид записи.", nameof(kind))
    };

    public static void Validate(ChatCapturedMedia media)
    {
        if (media.DurationMs is < 1 || media.DurationMs > MaxDurationMs(media.Kind)
            || media.Bytes.Length is < 12 || media.Bytes.Length > MaxBytes(media.Kind))
            throw new InvalidDataException("Запись превышает допустимый размер или длительность.");
    }
}

/// <summary>Windows camera/microphone capture shared by personal and group chats.</summary>
internal sealed class WindowsChatMediaRecorder : IChatMediaRecorder
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private MediaCapture? capture;
    private string? path;
    private string? kind;
    private Stopwatch? watch;

    public long CurrentLength
    {
        get
        {
            try { return path is { } current ? new FileInfo(current).Length : 0; }
            catch (IOException) { return 0; }
        }
    }

    public async Task StartAsync(string mediaKind, CancellationToken ct)
    {
        _ = ChatMediaLimits.MaxBytes(mediaKind);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new PlatformNotSupportedException();
        await gate.WaitAsync(ct);
        try
        {
            if (capture is not null) throw new InvalidOperationException("Запись уже идёт.");
            var temporary = Path.Combine(Path.GetTempPath(), $"zapara-chat-{Guid.NewGuid():N}" +
                (mediaKind == "voice" ? ".m4a" : ".mp4"));
            using (File.Create(temporary)) { }
            var candidate = new MediaCapture();
            var started = false;
            try
            {
                await candidate.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = mediaKind == "voice" ? StreamingCaptureMode.Audio : StreamingCaptureMode.AudioAndVideo
                });
                ct.ThrowIfCancellationRequested();
                var file = await StorageFile.GetFileFromPathAsync(temporary);
                var profile = mediaKind == "voice"
                    ? MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Low)
                    : MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Qvga);
                profile.Audio.Bitrate = 24_000;
                if (mediaKind == "circle") profile.Video.Bitrate = 450_000;
                await candidate.StartRecordToStorageFileAsync(profile, file);
                started = true;
                ct.ThrowIfCancellationRequested();
                capture = candidate;
                path = temporary;
                kind = mediaKind;
                watch = Stopwatch.StartNew();
            }
            catch
            {
                if (started)
                    try { await candidate.StopRecordAsync(); }
                    catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { }
                candidate.Dispose();
                DeleteTemporary(temporary);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<ChatCapturedMedia> FinishAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (capture is null || path is null || kind is null || watch is null)
                throw new InvalidOperationException("Запись не начата.");
            watch.Stop();
            var duration = checked((int)Math.Max(1, watch.ElapsedMilliseconds));
            var mediaKind = kind;
            var temporary = path;
            try
            {
                await capture.StopRecordAsync();
                ct.ThrowIfCancellationRequested();
                var size = new FileInfo(temporary).Length;
                if (size is < 12 || size > ChatMediaLimits.MaxBytes(mediaKind)
                    || duration > ChatMediaLimits.MaxDurationMs(mediaKind))
                    throw new InvalidDataException("Запись превышает допустимый размер или длительность.");
                var bytes = await File.ReadAllBytesAsync(temporary, ct);
                var result = new ChatCapturedMedia(mediaKind, Path.GetFileName(temporary), bytes, duration);
                try { ChatMediaLimits.Validate(result); }
                catch { CryptographicOperations.ZeroMemory(bytes); throw; }
                return result;
            }
            finally { ClearRecording(); }
        }
        finally { gate.Release(); }
    }

    public async Task CancelAsync()
    {
        await gate.WaitAsync();
        try
        {
            try { if (capture is not null) await capture.StopRecordAsync(); }
            finally { ClearRecording(); }
        }
        finally { gate.Release(); }
    }

    private void ClearRecording()
    {
        watch?.Stop();
        watch = null;
        capture?.Dispose();
        capture = null;
        kind = null;
        var old = path;
        path = null;
        if (old is not null) DeleteTemporary(old);
    }

    private static void DeleteTemporary(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
