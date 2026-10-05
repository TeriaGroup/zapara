using SkiaSharp;
using Vograph.Desktop.Features.Chat;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class AvatarUploadTests
{
    [Fact]
    public void Wide_camera_photo_becomes_small_square_jpeg()
    {
        using var source = new SKBitmap(1800, 1200);
        using (var canvas = new SKCanvas(source)) canvas.Clear(SKColors.Blue);
        using var image = SKImage.FromBitmap(source);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var output = AvatarUpload.Prepare(png.ToArray());
        Assert.InRange(output.Length, 1, 3 * 1024 * 1024);
        using var decoded = SKBitmap.Decode(output);
        Assert.Equal(512, decoded.Width);
        Assert.Equal(512, decoded.Height);
    }

    [Fact]
    public void Rejects_oversized_source_before_decoding()
        => Assert.Throws<InvalidDataException>(() => AvatarUpload.Prepare(new byte[AvatarUpload.MaxSourceBytes + 1]));
}
