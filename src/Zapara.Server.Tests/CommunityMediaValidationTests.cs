using Xunit;
using Microsoft.AspNetCore.Http;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class CommunityMediaValidationTests
{
    private static readonly byte[] M4a = [0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' '];
    private static readonly byte[] Mp4 = [0, 0, 0, 12, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Ogg = [(byte)'O', (byte)'g', (byte)'g', (byte)'S', 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Mp3 = [(byte)'I', (byte)'D', (byte)'3', 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Group_voice_accepts_personal_chat_audio_formats_with_declared_duration()
    {
        foreach (var bytes in new[] { M4a, Webm, Ogg, Mp3 })
        {
            CommunityMedia.ValidateRecording("voice", bytes, 1);
            CommunityMedia.ValidateRecording("voice", bytes, 180_000);
        }
    }

    [Fact]
    public void Group_circle_accepts_video_formats_but_rejects_audio_only_mp4_brand()
    {
        CommunityMedia.ValidateRecording("circle", Mp4, 1);
        CommunityMedia.ValidateRecording("circle", Webm, 60_000);

        var problem = Assert.Throws<CommunityServiceException>(() => CommunityMedia.ValidateRecording("circle", M4a, 1));
        Assert.Equal(400, problem.Status);
        Assert.Equal("invalid_request", problem.Code);
    }

    [Fact]
    public void Group_recording_rejects_invalid_duration_and_unknown_format()
    {
        foreach (var (kind, bytes, duration) in new (string, byte[], int?)[]
        {
            ("voice", M4a, null), ("circle", Mp4, null),
            ("voice", M4a, 0), ("voice", M4a, 180_001), ("circle", Mp4, 60_001),
            ("voice", new byte[12], 1), ("circle", new byte[12], 1), ("image", new byte[12], 1)
        })
        {
            var problem = Assert.Throws<CommunityServiceException>(() => CommunityMedia.ValidateRecording(kind, bytes, duration));
            Assert.Equal(400, problem.Status);
            Assert.Equal("invalid_request", problem.Code);
        }
    }

    [Theory]
    [InlineData("voice", null)]
    [InlineData("circle", null)]
    [InlineData("voice", "1.5")]
    [InlineData("image", "1")]
    public async Task Group_upload_rejects_missing_or_misplaced_duration_before_reading_bytes(string kind, string? duration)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/octet-stream";
        context.Request.Headers["X-Zapara-Kind"] = kind;
        context.Request.Headers["X-Zapara-Name"] = "recording.m4a";
        if (duration is not null) context.Request.Headers["X-Zapara-Duration-Ms"] = duration;
        context.Request.Body = new MemoryStream(M4a);

        var problem = await Assert.ThrowsAsync<CommunityInputException>(() => CommunityMedia.Post(context, "unused"));

        Assert.Equal(400, problem.Status);
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Theory]
    [InlineData("general")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Group_upload_rejects_invalid_channel_before_reading_media(string topic)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/octet-stream";
        context.Request.Headers["X-Zapara-Kind"] = "image";
        context.Request.Headers["X-Zapara-Name"] = "photo.webp";
        context.Request.Headers["X-Zapara-Topic"] = topic;
        context.Request.Body = new MemoryStream(Mp4);

        var problem = await Assert.ThrowsAsync<CommunityInputException>(() => CommunityMedia.Post(context, "unused"));

        Assert.Equal(400, problem.Status);
        Assert.Equal(0, context.Request.Body.Position);
    }

}
