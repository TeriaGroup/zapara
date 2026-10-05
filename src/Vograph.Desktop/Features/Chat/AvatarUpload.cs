using SkiaSharp;

namespace Vograph.Desktop.Features.Chat;

/// <summary>Crop a local camera photo to the server's square avatar shape before upload.</summary>
public static class AvatarUpload
{
    public const int MaxSourceBytes = 20 * 1024 * 1024;
    public static byte[] Prepare(byte[] source)
    {
        if (source is null || source.Length is < 1 or > MaxSourceBytes)
            throw new InvalidDataException("Фото слишком большое или пустое.");
        try
        {
            using var stream = new SKMemoryStream(source);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Неверный формат фото.");
            if (codec.Info.Width < 1 || codec.Info.Height < 1
                || (long)codec.Info.Width * codec.Info.Height > 48_000_000)
                throw new InvalidDataException("Слишком большое разрешение фото.");
            using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Не удалось открыть фото.");
            using var square = new SKBitmap(512, 512);
            using (var canvas = new SKCanvas(square))
            {
                canvas.Clear(SKColors.White);
                var side = Math.Min(bitmap.Width, bitmap.Height);
                var sourceRect = SKRect.Create((bitmap.Width - side) / 2f, (bitmap.Height - side) / 2f, side, side);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.Translate(256, 256);
                if (codec.EncodedOrigin == SKEncodedOrigin.RightTop) canvas.RotateDegrees(90);
                else if (codec.EncodedOrigin == SKEncodedOrigin.BottomRight) canvas.RotateDegrees(180);
                else if (codec.EncodedOrigin == SKEncodedOrigin.LeftBottom) canvas.RotateDegrees(270);
                canvas.Translate(-256, -256);
                canvas.DrawBitmap(bitmap, sourceRect, SKRect.Create(0, 0, 512, 512), paint);
            }
            using var image = SKImage.FromBitmap(square);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85)
                ?? throw new InvalidDataException("Не удалось подготовить фото.");
            if (encoded.Size > Vograph.Core.Services.Social.AvatarHttpClient.MaxUploadBytes)
                throw new InvalidDataException("Фото после обработки слишком большое.");
            return encoded.ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception) { throw new InvalidDataException("Не удалось открыть фото."); }
    }
}
