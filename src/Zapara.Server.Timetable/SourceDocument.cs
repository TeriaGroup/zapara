using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Vograph.Timetable;

namespace Zapara.Server.Timetable;

public enum SourceKind { File, Http }

public sealed class SourceDocument
{
    private readonly byte[] bytes;

    private SourceDocument(byte[] bytes, SourceKind kind, DateTimeOffset fetchedAt, DateTimeOffset? modifiedAt)
    {
        this.bytes = bytes;
        DecodedXml = TimetableParser.DecodeXml(bytes);
        SourceKind = kind;
        SourceUrl = kind == SourceKind.Http ? TimetableParser.DefaultUrl : null;
        SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        FetchedAtUtc = fetchedAt.ToUniversalTime();
        SourceModifiedAt = modifiedAt?.ToUniversalTime();
    }

    [JsonIgnore] public ReadOnlySpan<byte> Bytes => bytes;
    [JsonIgnore] public string DecodedXml { get; }
    public SourceKind SourceKind { get; }
    public string? SourceUrl { get; }
    public string SourceSha256 { get; }
    public DateTimeOffset FetchedAtUtc { get; }
    public DateTimeOffset? SourceModifiedAt { get; }

    public static SourceDocument Create(ReadOnlySpan<byte> bytes, SourceKind kind, TimeProvider timeProvider,
        DateTimeOffset? sourceModifiedAt = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (kind is not (SourceKind.File or SourceKind.Http))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == SourceKind.File && sourceModifiedAt is not null)
            throw new ArgumentException("Файловый источник не содержит upstream Last-Modified.");
        return new SourceDocument(bytes.ToArray(), kind, timeProvider.GetUtcNow(), sourceModifiedAt);
    }
}
