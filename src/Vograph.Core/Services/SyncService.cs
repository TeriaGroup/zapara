using System.Text.Json;
using Vograph.Core.Models;
using QRCoder;
using System.Net;
using System.Text;

namespace Vograph.Core.Services;

public class SyncService
{
    private readonly Database _db;
    public SyncService(Database db) => _db = db;

    public class SyncPayload
    {
        public int Version { get; set; } = 1;
        public DateTime ExportedAt { get; set; }
        public List<Override> Overrides { get; set; } = new();
        public List<Homework> Homework { get; set; } = new();
        public List<FriendGroup> Friends { get; set; } = new();
        public Settings Settings { get; set; } = new();
    }

    public string ExportToJson()
    {
        var payload = new SyncPayload
        {
            Version = 1,
            ExportedAt = DateTime.UtcNow,
            Overrides = _db.GetOverrides(),
            Homework = new HomeworkService(_db).GetAll(),
            Friends = _db.GetFriends(),
            Settings = _db.GetSettings()
        };
        var opts = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(payload, opts);
    }

    public void ExportToFile(string path)
    {
        var json = ExportToJson();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, json, Encoding.UTF8);
        // Update lastSyncAt
        var s = _db.GetSettings();
        s.LastSyncAt = DateTime.UtcNow;
        _db.SaveSettings(s);
    }

    public (int addedOverrides, int addedHomework, int addedFriends) ImportFromJson(string json, bool overwrite = true)
    {
        var payload = JsonSerializer.Deserialize<SyncPayload>(json);
        if (payload == null || payload.Version != 1) throw new Exception("Invalid sync payload version");
        int o = 0, h = 0, f = 0;

        // Merge overrides by normalized+scope: lastWriteWins
        var existingOv = _db.GetOverrides();
        foreach (var ov in payload.Overrides)
        {
            var match = existingOv.FirstOrDefault(x => x.SubjectRawNormalized == ov.SubjectRawNormalized && x.Scope == ov.Scope);
            if (match != null)
            {
                // compare CreatedAt: last wins
                if (ov.CreatedAt > match.CreatedAt)
                {
                    using var cmd = _db.Connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM overrides WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", match.Id);
                    cmd.ExecuteNonQuery();
                    var newOv = new Override { SubjectRawNormalized = ov.SubjectRawNormalized, Scope = ov.Scope, DisplayName = ov.DisplayName, Note = ov.Note, CreatedAt = ov.CreatedAt };
                    _db.InsertOverride(newOv);
                    o++;
                }
            }
            else
            {
                var newOv = new Override { SubjectRawNormalized = ov.SubjectRawNormalized, Scope = ov.Scope, DisplayName = ov.DisplayName, Note = ov.Note, CreatedAt = ov.CreatedAt };
                _db.InsertOverride(newOv);
                o++;
            }
        }

        // Merge homework by id? But ids may collide across devices; merge by subjectNormalized+text+createdAt
        var existingHw = new HomeworkService(_db).GetAll();
        foreach (var hw in payload.Homework)
        {
            var match = existingHw.FirstOrDefault(x => x.SubjectRawNormalized == hw.SubjectRawNormalized && x.Text == hw.Text && x.CreatedAt == hw.CreatedAt);
            if (match != null)
            {
                // lastWriteWins based on CreatedAt? For MVP, skip duplicate
                continue;
            }
            // Insert with new id
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO homework (subjectRawNormalized, text, createdAt, targetNthOccurrence, dueDateComputed, status, doneAt)
VALUES (@s,@t,@ca,@n,@due,@st,@da)";
            cmd.Parameters.AddWithValue("@s", hw.SubjectRawNormalized);
            cmd.Parameters.AddWithValue("@t", hw.Text);
            cmd.Parameters.AddWithValue("@ca", hw.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@n", hw.TargetNthOccurrence);
            cmd.Parameters.AddWithValue("@due", hw.DueDateComputed?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@st", hw.Status);
            cmd.Parameters.AddWithValue("@da", hw.DoneAt?.ToString("o") ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
            h++;
        }

        // Merge friends by groupName
        var existingFr = _db.GetFriends();
        foreach (var fr in payload.Friends)
        {
            if (existingFr.Any(x => x.GroupName == fr.GroupName)) continue;
            if (existingFr.Count >= 5) break;
            _db.InsertFriend(new FriendGroup { GroupName = fr.GroupName, ColorHex = fr.ColorHex, Enabled = fr.Enabled });
            f++;
        }

        // Merge settings lastSyncWins? Keep myGroupId if not set, else lastWriteWins by ExportedAt
        // For MVP, if payload.Settings.MyGroupId not null and current is null, adopt
        var curSettings = _db.GetSettings();
        if (string.IsNullOrEmpty(curSettings.MyGroupId) && !string.IsNullOrEmpty(payload.Settings.MyGroupId))
        {
            curSettings.MyGroupId = payload.Settings.MyGroupId;
        }
        // Merge notify times and strictness if payload newer? Use ExportedAt > LastSyncAt
        // Language: keep receiver's choice per doc (do not overwrite) — documented in docs/API.md §9
        // If you want to sync language, uncomment next line: curSettings.Language = payload.Settings.Language;
        if (payload.ExportedAt > (curSettings.LastSyncAt ?? DateTime.MinValue))
        {
            if (!string.IsNullOrEmpty(payload.Settings.NotifyTime1)) curSettings.NotifyTime1 = payload.Settings.NotifyTime1;
            if (!string.IsNullOrEmpty(payload.Settings.NotifyTime2)) curSettings.NotifyTime2 = payload.Settings.NotifyTime2;
            curSettings.IntersectionStrictness = payload.Settings.IntersectionStrictness;
            curSettings.ParityInvert = payload.Settings.ParityInvert;
            // language sync is optional; we preserve receiver's choice, but also support lastWriteWins if needed:
            // if (!string.IsNullOrEmpty(payload.Settings.Language)) curSettings.Language = payload.Settings.Language;
        }
        curSettings.LastSyncAt = DateTime.UtcNow;
        _db.SaveSettings(curSettings);

        // Recompute homework statuses after import
        try { new HomeworkService(_db).RecomputeAllStatuses(); } catch { }

        return (o, h, f);
    }

    public void ImportFromFile(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        ImportFromJson(json);
        var s = _db.GetSettings();
        s.LastSyncAt = DateTime.UtcNow;
        _db.SaveSettings(s);
    }

    public string GenerateQrContent(string json) => GenerateQrContent(json, GetLocalIp());

    /// <summary>The QR carries the export itself while it is small; otherwise a link to the LAN server. The host is a
    /// parameter so the caller can resolve it off the UI thread and outside the Core gate (the desktop does).</summary>
    public static string GenerateQrContent(string json, string ip) => json.Length < 1500 ? json : $"http://{ip}:8765/sync#token";

    public string GetLocalIp()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return ip?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    public void SaveQrImage(string content, string path, int pixelsPerModule = 10)
    {
        using var gen = new QRCodeGenerator();
        var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(pixelsPerModule);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path, bytes);
    }

}
