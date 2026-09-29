using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

public sealed class AvatarTests
{
    [Fact]
    public void Avatar_is_square_webp_and_has_no_camera_metadata()
    {
        using var source = new Image<Rgba32>(900, 600);
        source.Metadata.ExifProfile = new ExifProfile();
        source.Metadata.ExifProfile.SetValue(ExifTag.Artist, "private-camera-owner");
        using var input = new MemoryStream();
        source.SaveAsPng(input);
        var bytes = AvatarCompressor.Compress(input.ToArray());
        using var actual = Image.Load(bytes);
        Assert.Equal(512, actual.Width);
        Assert.Equal(512, actual.Height);
        Assert.Equal(1, actual.Frames.Count);
        Assert.Null(actual.Metadata.ExifProfile);
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.InRange(bytes.Length, 12, AvatarCompressor.MaxOutputBytes);
    }

    [Fact]
    public void Avatar_rejects_invalid_and_oversized_input()
    {
        Assert.Equal(400, Assert.Throws<SocialException>(() => AvatarCompressor.Compress(new byte[64])).Status);
        Assert.Equal(413, Assert.Throws<SocialException>(() => AvatarCompressor.Compress(new byte[3 * 1024 * 1024 + 1])).Status);
    }

    [Fact]
    public void Avatar_keeps_only_the_first_animation_frame()
    {
        using var source = new Image<Rgba32>(16, 16);
        source.Frames.AddFrame(source.Frames.RootFrame);
        using var input = new MemoryStream();
        source.SaveAsGif(input);
        using var actual = Image.Load(AvatarCompressor.Compress(input.ToArray()));
        Assert.Equal(1, actual.Frames.Count);
    }

    [Fact]
    public void Avatar_rejects_small_compressed_image_with_excessive_decoded_pixels()
    {
        using var source = new Image<Rgba32>(5001, 4800);
        using var input = new MemoryStream();
        source.SaveAsPng(input);
        Assert.True(input.Length < AvatarCompressor.MaxInputBytes);
        Assert.Equal(413, Assert.Throws<SocialException>(() => AvatarCompressor.Compress(input.ToArray())).Status);
    }
}
