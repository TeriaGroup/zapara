using System.IO;
using System.Text;
using System.Xml;
using Vograph.Core.Models;
using Vograph.Timetable;

namespace Vograph.Core.Services;


public class LecturerService
{
    public const string DefaultUrl = "https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableLecturer50.xml";
    private List<LecturerInfo> _lecturers = new();
    private List<LecturerLesson> _lessons = new();
    private bool _loaded = false;

    private HttpClient? _http;
    private HttpClient Http => _http ??= ParserService.CreateHttpClient();

    /// <param name="cachePath">Where a downloaded copy is kept (the desktop passes VOGRAPH_DATA_DIR\TimetableLecturer50.xml).</param>
    /// <param name="bundledPath">The copy shipped next to the exe for a first offline start.</param>
    public LecturerService(Database? db = null, string? cachePath = null, string? bundledPath = null)
    {
        _ = db;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vograph", "TimetableLecturer50.xml");
        BundledPath = bundledPath ?? Path.Combine(AppContext.BaseDirectory, "TimetableLecturer50.xml");
    }

    public string CachePath { get; }
    public string BundledPath { get; }

    public IReadOnlyList<LecturerInfo> Lecturers => _lecturers;
    public IReadOnlyList<LecturerLesson> Lessons => _lessons;
    public bool IsLoaded => _loaded;

    public async Task<(string xml, bool fromCache)> FetchXmlAsync(string url = DefaultUrl, HttpClient? client = null)
    {
        var http = client ?? Http;
        try
        {
            var bytes = await http.GetByteArrayAsync(url);
            var xml = ParserService.DecodeXml(bytes);
            if (TimetableParser.IsHtml(xml)) throw new InvalidOperationException(TimetableParser.NotTimetable);
            ValidateXml(xml);
            await SaveCacheAsync(xml);
            return (xml, false);
        }
        catch
        {
            if (File.Exists(CachePath))
            {
                try { var cached = await File.ReadAllTextAsync(CachePath, Encoding.UTF8); return (cached, true); } catch {}
            }
            throw;
        }
    }

    private static void ValidateXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "Timetable")
            throw new XmlException("Получен файл без расписания преподавателей.");
        while (reader.Read()) { }
    }

    private async Task SaveCacheAsync(string xml)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(CachePath))!);
            temporary = CachePath + "." + Guid.NewGuid().ToString("N") + ".part";
            await File.WriteAllTextAsync(temporary, xml, Encoding.UTF8);
            File.Move(temporary, CachePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task LoadAsync(string? xmlOverride = null)
    {
        string xml;
        if (xmlOverride != null) xml = xmlOverride;
        else
        {
            if (File.Exists(CachePath))
            {
                try { xml = await File.ReadAllTextAsync(CachePath, Encoding.UTF8); Parse(xml); _loaded = true; } catch {}
            }
            if (!_loaded)
            {
                var bundled = BundledPath;
                if (File.Exists(bundled))
                {
                    try { xml = await File.ReadAllTextAsync(bundled, Encoding.UTF8); Parse(xml); _loaded = true; try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!); File.Copy(bundled, CachePath, true); } catch {} } catch {}
                }
            }
            try
            {
                var (fetched, fromCache) = await FetchXmlAsync();
                if (!fromCache)
                {
                    Parse(fetched);
                    _loaded = true;
                }
            }
            catch
            {
                if (!_loaded && File.Exists(CachePath))
                {
                    var cached = await File.ReadAllTextAsync(CachePath, Encoding.UTF8);
                    Parse(cached);
                    _loaded = true;
                }
                else if (!_loaded)
                {
                    var bundled2 = BundledPath;
                    if (File.Exists(bundled2))
                    {
                        var bxml = await File.ReadAllTextAsync(bundled2, Encoding.UTF8);
                        Parse(bxml);
                        _loaded = true;
                    }
                }
            }
            return;
        }
        Parse(xml);
        _loaded = true;
    }

    public void Parse(string xml)
    {
        var parsed = LecturerXmlParser.Parse(xml);
        _lecturers = parsed.Lecturers;
        _lessons = parsed.Lessons;
    }

}
