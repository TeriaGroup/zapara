using System.Security.Cryptography;
using System.Text;

namespace Vograph.Core.Services;

/// <summary>Checks a downloaded release archive before it is unpacked over the app. Every Windows release carries
/// <see cref="ChecksumsAssetName"/> (sha256sum format) and, once the release key exists, its ECDSA P-256 signature
/// <see cref="SignatureAssetName"/>. See docs/RELEASE_SIGNING.md for the release side.</summary>
public static class UpdateVerifier
{
    public const string ChecksumsAssetName = "SHA256SUMS";
    public const string SignatureAssetName = "SHA256SUMS.sig";
    public const int MaxChecksumsBytes = 64 * 1024;
    public const int MaxSignatureBytes = 1024;

    /// <summary>Public half of the release signing key (SubjectPublicKeyInfo PEM, ECDSA P-256). While it is empty the
    /// archive is checked against SHA256SUMS only; once it is set, a valid signature of SHA256SUMS is required.</summary>
    public const string ReleasePublicKeyPem = "";

    public enum Outcome { Ok, NoChecksums, NotListed, Mismatch, NoSignature, BadSignature }

    public sealed record Result(Outcome Outcome, string? Sha256)
    {
        public bool Ok => Outcome == Outcome.Ok;
    }

    /// <summary>Lines of «&lt;64 hex&gt;  name» or «&lt;64 hex&gt; *name». Anything else is ignored; a name listed
    /// twice with different hashes makes the whole file unusable.</summary>
    public static IReadOnlyDictionary<string, string> ParseChecksums(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 66 || (line[64] != ' ' && line[64] != '\t')) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[65..];
            if (name.StartsWith(' ') || name.StartsWith('*')) name = name[1..];
            name = name.Trim();
            if (name.Length == 0) continue;
            hash = hash.ToLowerInvariant();
            if (map.TryGetValue(name, out var existing) && existing != hash) return new Dictionary<string, string>();
            map[name] = hash;
        }
        return map;
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>ECDSA P-256 / SHA-256 over the exact bytes of SHA256SUMS. Accepts DER (openssl dgst -sign) and the
    /// raw 64-byte r||s form.</summary>
    public static bool VerifySignature(byte[] data, byte[] signature, string publicKeyPem)
    {
        if (signature.Length == 0 || signature.Length > MaxSignatureBytes || string.IsNullOrWhiteSpace(publicKeyPem)) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize != 256) return false;
            var format = signature.Length == 64 ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation : DSASignatureFormat.Rfc3279DerSequence;
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, format);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Never throws for bad input; IO errors on <paramref name="filePath"/> propagate.</summary>
    public static Result Verify(string filePath, string assetName, byte[]? checksums, byte[]? signature, string publicKeyPem)
    {
        if (checksums is null || checksums.Length == 0 || checksums.Length > MaxChecksumsBytes) return new(Outcome.NoChecksums, null);
        if (!string.IsNullOrWhiteSpace(publicKeyPem))
        {
            if (signature is null || signature.Length == 0) return new(Outcome.NoSignature, null);
            if (!VerifySignature(checksums, signature, publicKeyPem)) return new(Outcome.BadSignature, null);
        }
        string text;
        try { text = new UTF8Encoding(false, true).GetString(checksums); }
        catch (DecoderFallbackException) { return new(Outcome.NoChecksums, null); }
        if (!ParseChecksums(text).TryGetValue(assetName, out var expected)) return new(Outcome.NotListed, null);
        var actual = Sha256File(filePath);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected))
            ? new(Outcome.Ok, actual)
            : new(Outcome.Mismatch, actual);
    }

    /// <summary>The asset name a download URL ends with (GitHub: …/releases/download/&lt;tag&gt;/&lt;name&gt;).</summary>
    public static string AssetNameFromUrl(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        var slash = path.LastIndexOf('/');
        return Uri.UnescapeDataString(slash >= 0 ? path[(slash + 1)..] : path);
    }
}
