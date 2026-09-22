using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vograph.Core.Campus;
using Vograph.Core.Services;
using Zapara.Client.Domain;

namespace Zapara.Server.Web;

public sealed record PublicAsset(string Url, string Sha256, int Bytes);
public sealed record PublicMap(string Id, string Building, int Floor, string Url, string Sha256, int Bytes);
public sealed record PublicMapsManifest(string Version, int GraphVersion, IReadOnlyList<PublicMap> Maps, PublicAsset Graph, PublicAsset Coordinates);
public sealed record PublicTeachers(string Version, IReadOnlyList<LecturerInfo> Lecturers, TeacherCatalogMetadata? Metadata = null);
public sealed record PublicTeacherTimetable(string Version, LecturerInfo Lecturer, IReadOnlyList<LecturerLesson> Lessons, TeacherCatalogMetadata? Metadata = null);
public sealed record PackagedAsset(byte[] Content, string ContentType, string Sha256);

public sealed class PackagedPublicCatalog
{
    private readonly Dictionary<string, PackagedAsset> _assets = new(StringComparer.Ordinal);
    private readonly LecturerCatalog _lecturers;
    public PublicTeachers Teachers { get; }
    public TeacherCatalogSnapshot TeacherSnapshot { get; }
    public PublicMapsManifest Maps { get; }

    public PackagedPublicCatalog(string root)
    {
        var xml = Read(Path.Combine(root, "TimetableLecturer50.xml"));
        _lecturers = LecturerCatalog.Parse(Encoding.UTF8.GetString(xml));
        if (_lecturers.Lecturers.Count == 0 || _lecturers.Lessons.Count == 0)
            throw new InvalidOperationException("Пакет преподавателей пуст.");
        TeacherSnapshot = new(Hash(xml), _lecturers, new("packaged", null, null, null));
        Teachers = TeacherSnapshot.Teachers;
        var plans = new List<PublicMap>();
        foreach (var building in new[] { "ГК", "УЛК" })
        for (var floor = 1; floor <= (building == "ГК" ? 4 : 5); floor++)
        {
            var filename = building == "ГК" ? $"karta-glavnyj-korpus-{floor}-etazh-2022.jpg" : $"karta-ulk.-{floor}-etazh-2022.jpg";
            var asset = Add(filename, "image/jpeg");
            var bytes = _assets[filename].Content;
            if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8)
                throw new InvalidOperationException("Некорректный план кампуса.");
            plans.Add(new($"{(building == "ГК" ? "gk" : "ulk")}-{floor}", building, floor, asset.Url, asset.Sha256, asset.Bytes));
        }
        var graphAsset = Add("campus-graph.json", "application/json; charset=utf-8");
        var graph = CampusGraph.Load(Encoding.UTF8.GetString(_assets["campus-graph.json"].Content));
        var coordinates = Add("coords.json", "application/json; charset=utf-8");
        using var coords = JsonDocument.Parse(_assets["coords.json"].Content);
        if (coords.RootElement.GetProperty("version").GetInt32() != 1 || coords.RootElement.GetProperty("maps").ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Некорректная версия координат кампуса.");
        var fingerprint = string.Join("\n", _assets.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}:{p.Value.Sha256}"));
        Maps = new(Hash(Encoding.UTF8.GetBytes(fingerprint)), graph.Version, plans, graphAsset, coordinates);

        PublicAsset Add(string filename, string contentType)
        {
            var bytes = Read(Path.Combine(root, "maps", filename));
            var hash = Hash(bytes);
            _assets.Add(filename, new(bytes, contentType, hash));
            return new("/api/v1/maps/assets/" + Uri.EscapeDataString(filename), hash, bytes.Length);
        }
    }

    public PublicTeacherTimetable? Teacher(string id)
    {
        var lecturer = _lecturers.Lecturers.FirstOrDefault(l => l.Id == id);
        return lecturer is null ? null : new(Teachers.Version, lecturer, _lecturers.LessonsOf(id));
    }

    public PackagedAsset? Asset(string filename) => _assets.GetValueOrDefault(filename);

    private static byte[] Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > 16777216) throw new InvalidOperationException("Недопустимый размер публичного ресурса.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
