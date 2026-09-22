using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Zapara.Client.Domain;

namespace Zapara.Server.Web;

public static class TeacherCatalogInput
{
    public const string SourceUrl = "https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableLecturer50.xml";
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly string[] Days = ["Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота"];

    // Only this client is used in production. No cookies, proxy credentials, redirects or decompression.
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false, UseProxy = false
    }) { Timeout = Timeout.InfiniteTimeSpan };

    internal static async Task<byte[]> FetchAsync(HttpClient client, TimeSpan timeout, TimeProvider clock, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
        Require(response.IsSuccessStatusCode && response.RequestMessage?.RequestUri == new Uri(SourceUrl));
        Require(!response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)));
        Require(response.Content.Headers.ContentLength is not > MaximumBytes);
        var media = response.Content.Headers.ContentType?.MediaType;
        Require(media is null or "application/xml" or "text/xml" or "application/octet-stream" or "text/plain");
        await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaximumBytes - (int)buffer.Length + 1)), linked.Token)) > 0)
        {
            Require(buffer.Length + read <= MaximumBytes);
            buffer.Write(chunk, 0, read);
        }
        linked.Token.ThrowIfCancellationRequested();
        Require(response.Content.Headers.ContentLength is not { } advertisedLength || advertisedLength == buffer.Length);
        return buffer.ToArray();
    }

    public static LecturerCatalog Validate(byte[] bytes)
    {
        Require(bytes.Length is > 0 and <= MaximumBytes);
        var xml = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes };
        // Bound depth before constructing the tree or invoking the shared, deliberately lenient parser.
        using (var reader = XmlReader.Create(new StringReader(xml), settings))
            while (reader.Read()) Require(reader.Depth <= 16);
        using var safe = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(safe);
        var root = doc.Root;
        Require(root?.Name == "Timetable");
        Require(!root!.DescendantsAndSelf().Any(node => node.Name.NamespaceName.Length != 0));
        Require(root.Elements().All(node => node.Name.LocalName is "Period" or "Weeks" or "Lecturer"));
        var teachers = root.Elements("Lecturer").ToArray();
        Require(teachers.Length is > 0 and <= 5000);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var lessonCount = 0;
        foreach (var teacher in teachers)
        {
            var id = (string?)teacher.Attribute("IdLecturer");
            Require(Text(id, 128) && ids.Add(id!));
            Require(Text((string?)teacher.Attribute("LecturerName"), 512));
            Require(((string?)teacher.Attribute("Kafedra"))?.Length is null or <= 512);
            Require(teacher.Elements().All(node => node.Name == "Days") && teacher.Elements("Days").Count() <= 1);
            var days = teacher.Element("Days");
            if (days is null) continue;
            Require(days.Elements().All(node => node.Name == "Day"));
            foreach (var day in days.Elements("Day"))
            {
                Require(Days.Contains((string?)day.Attribute("Title"), StringComparer.Ordinal));
                Require(day.Elements().All(node => node.Name == "LecturerLessons") && day.Elements("LecturerLessons").Count() == 1);
                var lessons = day.Element("LecturerLessons")!;
                Require(lessons.Elements().All(node => node.Name == "Lesson"));
                foreach (var lesson in lessons.Elements("Lesson"))
                {
                    Require(++lessonCount <= 100000);
                    Require(lesson.Elements().All(node => node.Name.LocalName is "DayTitle" or "WeekCode" or "Time" or "Discipline" or "Classroom" or "Groups"));
                    Require(lesson.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1));
                    Require(lesson.Element("WeekCode")?.Value.Trim() is "0" or "1" or "2");
                    Require(Text(lesson.Element("Time")?.Value, 128) && Text(lesson.Element("Discipline")?.Value, 4096));
                    Require(lesson.Element("Classroom")?.Value.Length is null or <= 1024);
                    var groups = lesson.Element("Groups");
                    if (groups is null) continue;
                    Require(groups.Elements().All(node => node.Name == "Group") && groups.Elements().Count() <= 100);
                    foreach (var group in groups.Elements())
                    {
                        Require(group.Elements().All(node => node.Name.LocalName is "IdGroup" or "Number") && group.Elements().GroupBy(node => node.Name).All(g => g.Count() == 1));
                        Require(Text(group.Element("IdGroup")?.Value, 128) || Text(group.Element("Number")?.Value, 128));
                    }
                }
            }
        }
        Require(lessonCount > 0);
        var catalog = LecturerCatalog.Parse(xml);
        Require(catalog.Lecturers.Count == teachers.Length && catalog.Lessons.Count == lessonCount);
        foreach (var lesson in catalog.Lessons)
        {
            Require(lesson.DayOfWeek is >= 1 and <= 6 && lesson.Parity is >= 0 and <= 2 && ids.Contains(lesson.LecturerId));
            Require(TimeOnly.TryParseExact(lesson.TimeStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start));
            Require(TimeOnly.TryParseExact(lesson.TimeEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && end > start);
        }
        return catalog;
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    private static void Require(bool valid) { if (!valid) throw new InvalidDataException("source_rejected"); }
}
