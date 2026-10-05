using Avalonia.Media.Imaging;

namespace Vograph.Desktop.Features.Chat;

internal static class ChatMediaPreview
{
    public static Bitmap DecodePhoto(byte[] bytes)
    {
        if (bytes.Length is < 12 or > 8 * 1024 * 1024) throw new InvalidDataException("Фото недоступно.");
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = Bitmap.DecodeToWidth(stream, 320, BitmapInterpolationMode.MediumQuality);
        if (bitmap.PixelSize.Width is < 1 or > 4096 || bitmap.PixelSize.Height is < 1 or > 4096)
        {
            bitmap.Dispose();
            throw new InvalidDataException("Фото недоступно.");
        }
        return bitmap;
    }
}
