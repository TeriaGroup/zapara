using Vograph.Desktop.Features.Chat;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ChatMediaPlayerTests
{
    [Fact]
    public void Group_media_without_content_type_keeps_its_recorded_container()
    {
        byte[] webm = [0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8];
        byte[] mp4 = [0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

        Assert.Equal(".webm", ChatMediaPlayer.ExtensionFor("voice", null, webm));
        Assert.Equal(".webm", ChatMediaPlayer.ExtensionFor("circle", null, webm));
        Assert.Equal(".m4a", ChatMediaPlayer.ExtensionFor("voice", null, mp4));
        Assert.Equal(".mp4", ChatMediaPlayer.ExtensionFor("circle", null, mp4));
    }

    [Fact]
    public void Stale_playback_sweep_removes_only_old_app_owned_media()
    {
        using var directory = new ProfileTestDirectory();
        var cache = Path.Combine(directory.Root, "playback");
        Directory.CreateDirectory(cache);
        var oldMedia = Path.Combine(cache, $"zapara-chat-play-{Guid.NewGuid():N}.mp4");
        var freshMedia = Path.Combine(cache, $"zapara-chat-play-{Guid.NewGuid():N}.m4a");
        var unrelated = Path.Combine(cache, "meeting.mp4");
        var impostor = Path.Combine(cache, "zapara-chat-play-important.mp4");
        File.WriteAllBytes(oldMedia, [1]);
        File.WriteAllBytes(freshMedia, [2]);
        File.WriteAllBytes(unrelated, [3]);
        File.WriteAllBytes(impostor, [4]);
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(oldMedia, now.AddHours(-3).UtcDateTime);
        File.SetLastWriteTimeUtc(freshMedia, now.AddMinutes(-20).UtcDateTime);
        File.SetLastWriteTimeUtc(unrelated, now.AddHours(-3).UtcDateTime);
        File.SetLastWriteTimeUtc(impostor, now.AddHours(-3).UtcDateTime);

        ChatMediaPlayer.SweepStaleFiles(cache, now.AddHours(-1));

        Assert.False(File.Exists(oldMedia));
        Assert.True(File.Exists(freshMedia));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(impostor));
    }

    [Fact]
    public async Task Playback_cleanup_retries_a_locked_file_then_deletes_it()
    {
        using var directory = new ProfileTestDirectory();
        var path = Path.Combine(directory.Root, $"zapara-chat-play-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, [1]);
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var deleting = ChatMediaPlayer.DeleteWithRetryAsync(path, 10, TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);
        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(path));
        held.Dispose();

        Assert.True(await deleting);
        Assert.False(File.Exists(path));
    }
}
