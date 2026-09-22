using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vograph.Core.Services;
using Zapara.Client.Domain;

namespace Zapara.Server.Web;

public sealed record TeacherCatalogMetadata(string Source, DateTimeOffset? FetchedAt, DateTimeOffset? LastAttemptAt,
    string? LastFailure, string CacheLifetime = "memory");

/// <summary>A captured generation keeps directory, lessons and metadata together across publication.</summary>
public sealed record TeacherCatalogSnapshot(string Version, LecturerCatalog Catalog, TeacherCatalogMetadata Metadata)
{
    public PublicTeachers Teachers => new(Version, Catalog.Lecturers, Metadata);
    public string RepresentationVersion => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Version + JsonSerializer.Serialize(Metadata)))).ToLowerInvariant();
    public PublicTeacherTimetable? Teacher(string id)
    {
        var teacher = Catalog.Lecturers.FirstOrDefault(value => value.Id == id);
        return teacher is null ? null : new(Version, teacher, Catalog.LessonsOf(id), Metadata);
    }
}

public sealed class TeacherCatalogStore(TeacherCatalogSnapshot packaged, TimeProvider clock)
{
    private TeacherCatalogSnapshot current = packaged;
    private readonly SemaphoreSlim publication = new(1, 1);
    public TeacherCatalogSnapshot Capture() => Volatile.Read(ref current);

    public async Task<bool> RefreshAsync(HttpClient client, TimeSpan timeout, CancellationToken ct = default)
    {
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!await publication.WaitAsync(0, ct)) return false;
        var attempt = clock.GetUtcNow();
        try
        {
            var bytes = await TeacherCatalogInput.FetchAsync(client, timeout, clock, ct);
            var parsed = TeacherCatalogInput.Validate(bytes);
            ct.ThrowIfCancellationRequested();
            Volatile.Write(ref current, new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), parsed,
                new("university", clock.GetUtcNow(), attempt, null)));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Failed("cancelled");
            throw;
        }
        catch (OperationCanceledException) { Failed("source_timeout"); return false; }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or System.Xml.XmlException or ArgumentException)
        { Failed("source_rejected"); return false; }
        finally { publication.Release(); }

        void Failed(string code)
        {
            var previous = Capture();
            Volatile.Write(ref current, previous with { Metadata = previous.Metadata with { LastAttemptAt = attempt, LastFailure = code } });
        }
    }
}
