using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Zapara.Server.Social;

public static class AvatarCompressor
{
    public const int MaxInputBytes = 3 * 1024 * 1024;
    public const int MaxOutputBytes = 512 * 1024;
    public const int MaxPixels = 24_000_000;
    public const int Edge = 512;

    public static byte[] Compress(ReadOnlySpan<byte> input)
    {
        if (input.Length > MaxInputBytes) throw new SocialException(413, "payload_too_large");
        if (input.Length < 12) throw new SocialException(400, "invalid_image");
        try
        {
            // Identification and decoding both stop after one frame, including animated uploads.
            var options = new DecoderOptions { MaxFrames = 1 };
            var info = Image.Identify(options, input);
            if (info.Width < 1 || info.Height < 1) throw new SocialException(400, "invalid_image");
            if ((long)info.Width * info.Height > MaxPixels) throw new SocialException(413, "payload_too_large");
            using var image = Image.Load(options, input);
            image.Mutate(operation => operation.AutoOrient().Resize(new ResizeOptions
            {
                Mode = ResizeMode.Crop,
                Size = new Size(Edge, Edge),
                Sampler = KnownResamplers.Lanczos3
            }));
            using var output = new MemoryStream();
            foreach (var quality in new[] { 80, 60, 40 })
            {
                output.SetLength(0);
                image.Save(output, new WebpEncoder
                {
                    Quality = quality,
                    FileFormat = WebpFileFormatType.Lossy,
                    SkipMetadata = true
                });
                if (output.Length is >= 12 and <= MaxOutputBytes) return output.ToArray();
            }
            throw new SocialException(413, "payload_too_large");
        }
        catch (SocialException) { throw; }
        catch (Exception exception) when (exception is ImageFormatException or UnknownImageFormatException or InvalidImageContentException or ArgumentException or NotSupportedException)
        { throw new SocialException(400, "invalid_image"); }
    }
}
