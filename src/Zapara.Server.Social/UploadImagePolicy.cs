using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Zapara.Server.Social;

/// <summary>Decoding rules shared by every user-uploaded image (chat photos and avatars).
/// Only JPEG, PNG and WebP decoders are registered, so any other container is rejected before a decoder runs.</summary>
public static class UploadImagePolicy
{
    public const string UnsupportedFormat = "unsupported_image_format";

    /// <summary>Longest accepted side in pixels, independent of the total pixel budget (blocks 1×N strips).</summary>
    public const int MaxDimension = 16_384;

    private static readonly Configuration Restricted = new(
        new JpegConfigurationModule(),
        new PngConfigurationModule(),
        new WebpConfigurationModule());

    /// <summary>Identification and decoding stop after the first frame, including animated uploads.</summary>
    public static DecoderOptions Options() => new() { Configuration = Restricted, MaxFrames = 1 };

    /// <summary>Size, format and pixel-count checks that run on the header only, before any pixel data is decoded.</summary>
    public static ImageInfo Inspect(ReadOnlySpan<byte> input, int maxBytes, long maxPixels)
    {
        if (input.Length < 12) throw new SocialException(400, "invalid_image");
        if (input.Length > maxBytes) throw new SocialException(413, "payload_too_large");
        var options = Options();
        ImageInfo info;
        try
        {
            info = Image.Identify(options, input);
        }
        catch (UnknownImageFormatException)
        {
            // The restricted configuration does not know the format. Header sniffing with the default
            // configuration tells a real but disallowed image (415) apart from garbage (400); it only reads magic bytes.
            if (IsKnownImage(input)) throw new SocialException(415, UnsupportedFormat);
            throw new SocialException(400, "invalid_image");
        }
        if (info.Width < 1 || info.Height < 1) throw new SocialException(400, "invalid_image");
        if (info.Width > MaxDimension || info.Height > MaxDimension || (long)info.Width * info.Height > maxPixels)
            throw new SocialException(413, "payload_too_large");
        return info;
    }

    private static bool IsKnownImage(ReadOnlySpan<byte> input)
    {
        try { return Image.DetectFormat(input) is not null; }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException or ArgumentException)
        { return false; }
    }
}
