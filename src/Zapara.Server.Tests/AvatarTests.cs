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
        source.SaveAsWebp(input);
        using (var animated = Image.Load(input.ToArray())) Assert.Equal(2, animated.Frames.Count);
        using var actual = Image.Load(AvatarCompressor.Compress(input.ToArray()));
        Assert.Equal(1, actual.Frames.Count);
    }

    [Fact]
    public void Avatar_accepts_only_jpeg_png_and_webp()
    {
        using var source = new Image<Rgba32>(32, 32);
        foreach (var encode in new Action<Stream>[] { s => source.SaveAsJpeg(s), s => source.SaveAsPng(s), s => source.SaveAsWebp(s) })
        {
            using var allowed = new MemoryStream();
            encode(allowed);
            Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(AvatarCompressor.Compress(allowed.ToArray()), 8, 4));
        }
        foreach (var encode in new Action<Stream>[] { s => source.SaveAsGif(s), s => source.SaveAsBmp(s), s => source.SaveAsTiff(s), s => source.SaveAsTga(s) })
        {
            using var refused = new MemoryStream();
            encode(refused);
            var error = Assert.Throws<SocialException>(() => AvatarCompressor.Compress(refused.ToArray()));
            Assert.Equal(415, error.Status);
            Assert.Equal(UploadImagePolicy.UnsupportedFormat, error.Code);
        }
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
