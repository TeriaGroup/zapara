using Microsoft.Data.Sqlite;
using Vograph.Core.Models;
using Vograph.Core.Services.Sync;
using Zapara.Contracts.Sync;

namespace Vograph.Core.Services;

public class Database : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _conn;

    public Database(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        try
        {
            _conn.Open();
            // Enable WAL for better concurrency
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
            EnsureSchema();
            TimetableApiCache.EnsureSchema(_conn);
        }
        catch
        {
            SqliteConnection.ClearPool(_conn);
            _conn.Dispose();
            throw;
        }
    }

    public SqliteConnection Connection => _conn;
    public bool UseApiCatalog { get; set; }
    public PrivateSyncOutbox? PrivateOutbox { get; set; }

    private void EnsureSchema()
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS groups (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    url TEXT,
    lastFetchedAt TEXT,
    rawXml TEXT
);
CREATE TABLE IF NOT EXISTS schedule_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    groupId TEXT NOT NULL,
    dayOfWeek INTEGER NOT NULL,
    parity INTEGER NOT NULL,
    idx INTEGER NOT NULL,
    timeStart TEXT,
    timeEnd TEXT,
    subjectRaw TEXT,
    subjectNormalized TEXT,
    teacherRaw TEXT,
    roomRaw TEXT,
    buildingRaw TEXT,
    typeRaw TEXT,
    classroomRaw TEXT,
    rawXml TEXT,
    FOREIGN KEY(groupId) REFERENCES groups(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_schedule_group_day_parity ON schedule_cache(groupId, dayOfWeek, parity);
CREATE TABLE IF NOT EXISTS overrides (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    subjectRawNormalized TEXT NOT NULL,
    scope TEXT NOT NULL,
    displayName TEXT NOT NULL,
    note TEXT,
    createdAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_overrides_subject_scope ON overrides(subjectRawNormalized, scope);
CREATE TABLE IF NOT EXISTS homework (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    subjectRawNormalized TEXT NOT NULL,
    text TEXT NOT NULL,
    createdAt TEXT NOT NULL,
    targetNthOccurrence INTEGER NOT NULL,
    dueDateComputed TEXT,
    status TEXT NOT NULL,
    doneAt TEXT
);
CREATE INDEX IF NOT EXISTS idx_homework_subject ON homework(subjectRawNormalized);
CREATE TABLE IF NOT EXISTS friends (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    groupName TEXT NOT NULL,
    colorHex TEXT NOT NULL,
    enabled INTEGER NOT NULL,
    memberNames TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS settings (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    myGroupId TEXT,
    parityInvert INTEGER NOT NULL DEFAULT 0,
    notifyTime1 TEXT,
    notifyTime2 TEXT,
    intersectionStrictness INTEGER NOT NULL DEFAULT 25,
    language TEXT NOT NULL DEFAULT 'ru',
    lastSyncAt TEXT,
    lastFetchedAt TEXT,
    lastAutoCheckAt TEXT,
    weekCount INTEGER NOT NULL DEFAULT 2,
    periodTitle TEXT,
    periodStart TEXT,
    mapPanelWidth INTEGER NOT NULL DEFAULT 300,
    alwaysShowAllTrafficLights INTEGER NOT NULL DEFAULT 0
);
INSERT OR IGNORE INTO settings (id, parityInvert, intersectionStrictness, weekCount, language) VALUES (1, 0, 25, 2, 'ru');
-- migrations for existing DBs (v2 -> v3)
-- add columns if missing (no error if exists, use try via separate statements executed below)
";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        // lightweight migrations: add missing columns safely
        TryAddColumn("settings", "language", "TEXT NOT NULL DEFAULT 'ru'");
        TryAddColumn("settings", "lastAutoCheckAt", "TEXT");
        TryAddColumn("settings", "mapPanelWidth", "INTEGER NOT NULL DEFAULT 300");
        TryAddColumn("settings", "alwaysShowAllTrafficLights", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn("settings", "autoUpdate", "INTEGER NOT NULL DEFAULT 1");
        PrivateSyncSchema.Ensure(_conn);
        // Migrate old strictness 50 (old default) to 25 (new default = "в вузе" visible) — buildings are close, red for "в вузе" was confusing
        try { using var c = _conn.CreateCommand(); c.CommandText = "UPDATE settings SET intersectionStrictness=25 WHERE intersectionStrictness=50"; c.ExecuteNonQuery(); } catch {}
    }

    private void TryAddColumn(string table, string column, string definition)
    {
        try
        {
            using var c = _conn.CreateCommand();
            c.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            c.ExecuteNonQuery();
        }
        catch { /* column already exists */ }
    }

    public Settings GetSettings()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT myGroupId, parityInvert, notifyTime1, notifyTime2, intersectionStrictness, language, lastSyncAt, lastFetchedAt, lastAutoCheckAt, weekCount, periodTitle, periodStart, mapPanelWidth, alwaysShowAllTrafficLights, autoUpdate, revision FROM settings WHERE id=1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new Settings();
        int mapW = 300;
        try { mapW = r.IsDBNull(12) ? 300 : r.GetInt32(12); } catch { try { mapW = (int)r.GetInt64(12); } catch { mapW = 300; } }
        if (mapW < 220) mapW = 220; if (mapW > 620) mapW = 620;
        bool alwaysShow = false;
        try { alwaysShow = !r.IsDBNull(13) && r.GetInt32(13) != 0; } catch { try { alwaysShow = !r.IsDBNull(13) && r.GetInt64(13) != 0; } catch {} }
        bool autoUpdate = true;
        try { autoUpdate = r.IsDBNull(14) || r.GetInt64(14) != 0; } catch { try { autoUpdate = r.IsDBNull(14) || r.GetInt32(14) != 0; } catch {} }
        long settingsRevision = 0;
        if (r.FieldCount > 15 && !r.IsDBNull(15)) settingsRevision = r.GetInt64(15);
        var settings = new Settings
        {
            MyGroupId = r.IsDBNull(0) ? null : r.GetString(0),
            ParityInvert = r.GetInt32(1) != 0,
            NotifyTime1 = r.IsDBNull(2) ? null : r.GetString(2),
            NotifyTime2 = r.IsDBNull(3) ? null : r.GetString(3),
            IntersectionStrictness = r.GetInt32(4),
            Language = r.IsDBNull(5) ? "ru" : r.GetString(5),
            LastSyncAt = r.IsDBNull(6) ? null : DateTime.TryParse(r.GetString(6), out var dt) ? dt : null,
            LastFetchedAt = r.IsDBNull(7) ? null : r.GetString(7),
            LastAutoCheckAt = r.IsDBNull(8) ? null : r.GetString(8),
            WeekCount = r.GetInt32(9),
            PeriodTitle = r.IsDBNull(10) ? null : r.GetString(10),
            PeriodStart = r.IsDBNull(11) ? null : r.GetString(11),
            MapPanelWidth = mapW,
            AlwaysShowAllTrafficLights = alwaysShow,
            AutoUpdate = autoUpdate,
            Revision = settingsRevision,
            EntityId = SyncValidation.SettingsId
        };
        r.Close();
        var metadata = new TimetableApiCache(this).Read(settings.MyGroupId);
        if (metadata is not null)
        {
            settings.PeriodStart = metadata.Period.Start.ToString("yyyy-MM-dd");
            settings.PeriodTitle = metadata.Period.Title;
            settings.WeekCount = metadata.Period.WeekCount;
            settings.LastFetchedAt = metadata.FetchedAt;
        }
        else if (UseApiCatalog) settings.LastFetchedAt = null;
        return settings;
    }

    public void SaveSettings(Settings s)
    {
        if (PrivateOutbox is { Enabled: true } && SettingsSyncChanged(s))
        {
            PrivateOutbox.InTransaction(() =>
            {
                var revision = WriteSettings(s);
                PrivateOutbox.Enqueue(Guid.NewGuid(), "settings", SyncValidation.SettingsId, revision,
                    "upsert", PrivateSyncMapper.Settings(s), 1);
                return 0;
            });
            return;
        }
        WriteSettings(s);
    }

    private bool SettingsSyncChanged(Settings s)
    {
        var cur = GetSettings();
        return cur.MyGroupId != s.MyGroupId || cur.ParityInvert != s.ParityInvert
            || cur.NotifyTime1 != s.NotifyTime1 || cur.NotifyTime2 != s.NotifyTime2
            || cur.IntersectionStrictness != s.IntersectionStrictness
            || cur.AlwaysShowAllTrafficLights != s.AlwaysShowAllTrafficLights;
    }

    private long WriteSettings(Settings s)
    {
        using var cmd = _conn.CreateCommand();
        int mapW = s.MapPanelWidth; if (mapW < 220) mapW = 220; if (mapW > 620) mapW = 620;
        cmd.CommandText = @"
UPDATE settings SET
    myGroupId=@g, parityInvert=@inv, notifyTime1=@t1, notifyTime2=@t2,
    intersectionStrictness=@strict, language=@lang, lastSyncAt=@sync, lastFetchedAt=@lf, lastAutoCheckAt=@lac,
    weekCount=@wc, periodTitle=@pt, periodStart=@ps, mapPanelWidth=@mpw, alwaysShowAllTrafficLights=@all,
    autoUpdate=@au, entityUuid=@u
WHERE id=1";
        cmd.Parameters.AddWithValue("@g", (object?)s.MyGroupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inv", s.ParityInvert ? 1 : 0);
        cmd.Parameters.AddWithValue("@t1", (object?)s.NotifyTime1 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t2", (object?)s.NotifyTime2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@strict", s.IntersectionStrictness);
        cmd.Parameters.AddWithValue("@lang", s.Language ?? "ru");
        cmd.Parameters.AddWithValue("@sync", s.LastSyncAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@lf", (object?)s.LastFetchedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lac", (object?)s.LastAutoCheckAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@wc", s.WeekCount);
        cmd.Parameters.AddWithValue("@pt", (object?)s.PeriodTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ps", (object?)s.PeriodStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mpw", mapW);
        cmd.Parameters.AddWithValue("@all", s.AlwaysShowAllTrafficLights ? 1 : 0);
        cmd.Parameters.AddWithValue("@au", s.AutoUpdate ? 1 : 0);
        cmd.Parameters.AddWithValue("@u", SyncValidation.SettingsId.ToString("D"));
        cmd.ExecuteNonQuery();
        return s.Revision;
    }

    public void UpsertGroup(Group g)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO groups (id, name, url, lastFetchedAt, rawXml)
VALUES (@id,@name,@url,@lf,@raw)
ON CONFLICT(id) DO UPDATE SET name=excluded.name, url=excluded.url, lastFetchedAt=excluded.lastFetchedAt, rawXml=excluded.rawXml";
        cmd.Parameters.AddWithValue("@id", g.Id);
        cmd.Parameters.AddWithValue("@name", g.Name);
        cmd.Parameters.AddWithValue("@url", (object?)g.Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lf", g.LastFetchedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@raw", (object?)g.RawXml ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        using var invalidate = _conn.CreateCommand();
        invalidate.CommandText = "DELETE FROM api_cache_metadata WHERE groupId=@id";
        invalidate.Parameters.AddWithValue("@id", g.Id);
        invalidate.ExecuteNonQuery();
    }

    public List<Group> GetAllGroups()
    {
        var list = new List<Group>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = UseApiCatalog && new TimetableApiCache(this).Read("") is not null
            ? "SELECT g.id,g.name,g.url,g.lastFetchedAt FROM groups g JOIN api_catalog a ON a.groupId=g.id ORDER BY g.name"
            : "SELECT id, name, url, lastFetchedAt FROM groups ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Group
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Url = r.IsDBNull(2) ? "" : r.GetString(2),
                LastFetchedAt = r.IsDBNull(3) ? null : DateTime.TryParse(r.GetString(3), out var dt) ? dt : null
            });
        }
        return list;
    }

    public Group? GetGroup(string id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, url, lastFetchedAt FROM groups WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Group { Id = r.GetString(0), Name = r.GetString(1), Url = r.IsDBNull(2) ? "" : r.GetString(2), LastFetchedAt = r.IsDBNull(3) ? null : DateTime.TryParse(r.GetString(3), out var dt) ? dt : null };
    }

    public void ClearScheduleForGroup(string groupId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM schedule_cache WHERE groupId=@gid";
        cmd.Parameters.AddWithValue("@gid", groupId);
        cmd.ExecuteNonQuery();
    }

    public void InsertLesson(Lesson l)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO schedule_cache
(groupId, dayOfWeek, parity, idx, timeStart, timeEnd, subjectRaw, subjectNormalized, teacherRaw, roomRaw, buildingRaw, typeRaw, classroomRaw, rawXml)
VALUES (@gid,@dow,@par,@idx,@ts,@te,@sub,@norm,@teach,@room,@build,@type,@cls,@raw)";
        cmd.Parameters.AddWithValue("@gid", l.GroupId);
        cmd.Parameters.AddWithValue("@dow", l.DayOfWeek);
        cmd.Parameters.AddWithValue("@par", l.Parity);
        cmd.Parameters.AddWithValue("@idx", l.Index);
        cmd.Parameters.AddWithValue("@ts", (object?)l.TimeStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@te", (object?)l.TimeEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sub", (object?)l.SubjectRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@norm", (object?)l.SubjectNormalized ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@teach", (object?)l.TeacherRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@room", (object?)l.RoomRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@build", (object?)l.BuildingRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", (object?)l.TypeRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cls", (object?)l.ClassroomRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@raw", DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<Lesson> GetLessons(string groupId, int dayOfWeek, int parity)
    {
        var list = new List<Lesson>();
        using var cmd = _conn.CreateCommand();
        // parity 0 means both, but our data uses 1/2; filter accordingly
        if (parity == 0)
            cmd.CommandText = "SELECT id, groupId, dayOfWeek, parity, idx, timeStart, timeEnd, subjectRaw, subjectNormalized, teacherRaw, roomRaw, buildingRaw, typeRaw, classroomRaw FROM schedule_cache WHERE groupId=@gid AND dayOfWeek=@dow ORDER BY idx, timeStart";
        else
            cmd.CommandText = "SELECT id, groupId, dayOfWeek, parity, idx, timeStart, timeEnd, subjectRaw, subjectNormalized, teacherRaw, roomRaw, buildingRaw, typeRaw, classroomRaw FROM schedule_cache WHERE groupId=@gid AND dayOfWeek=@dow AND (parity=@par OR parity=0) ORDER BY idx, timeStart";
        cmd.Parameters.AddWithValue("@gid", groupId);
        cmd.Parameters.AddWithValue("@dow", dayOfWeek);
        if (parity != 0) cmd.Parameters.AddWithValue("@par", parity);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Lesson
            {
                Id = r.GetInt64(0),
                GroupId = r.GetString(1),
                DayOfWeek = r.GetInt32(2),
                Parity = r.GetInt32(3),
                Index = r.GetInt32(4),
                TimeStart = r.IsDBNull(5) ? "" : r.GetString(5),
                TimeEnd = r.IsDBNull(6) ? "" : r.GetString(6),
                SubjectRaw = r.IsDBNull(7) ? "" : r.GetString(7),
                SubjectNormalized = r.IsDBNull(8) ? "" : r.GetString(8),
                TeacherRaw = r.IsDBNull(9) ? "" : r.GetString(9),
                RoomRaw = r.IsDBNull(10) ? "" : r.GetString(10),
                BuildingRaw = r.IsDBNull(11) ? "" : r.GetString(11),
                TypeRaw = r.IsDBNull(12) ? "" : r.GetString(12),
                ClassroomRaw = r.IsDBNull(13) ? "" : r.GetString(13)
            });
        }
        return list;
    }

    public List<Lesson> GetAllLessonsForGroup(string groupId)
    {
        var list = new List<Lesson>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, groupId, dayOfWeek, parity, idx, timeStart, timeEnd, subjectRaw, subjectNormalized, teacherRaw, roomRaw, buildingRaw, typeRaw, classroomRaw FROM schedule_cache WHERE groupId=@gid ORDER BY dayOfWeek, parity, idx";
        cmd.Parameters.AddWithValue("@gid", groupId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Lesson
            {
                Id = r.GetInt64(0),
                GroupId = r.GetString(1),
                DayOfWeek = r.GetInt32(2),
                Parity = r.GetInt32(3),
                Index = r.GetInt32(4),
                TimeStart = r.IsDBNull(5) ? "" : r.GetString(5),
                TimeEnd = r.IsDBNull(6) ? "" : r.GetString(6),
                SubjectRaw = r.IsDBNull(7) ? "" : r.GetString(7),
                SubjectNormalized = r.IsDBNull(8) ? "" : r.GetString(8),
                TeacherRaw = r.IsDBNull(9) ? "" : r.GetString(9),
                RoomRaw = r.IsDBNull(10) ? "" : r.GetString(10),
                BuildingRaw = r.IsDBNull(11) ? "" : r.GetString(11),
                TypeRaw = r.IsDBNull(12) ? "" : r.GetString(12),
                ClassroomRaw = r.IsDBNull(13) ? "" : r.GetString(13)
            });
        }
        return list;
    }

    public long InsertOverride(Override o)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO overrides (subjectRawNormalized, scope, displayName, note, createdAt, entityUuid, revision, tombstone, createdAtUtc) VALUES (@s,@scope,@d,@n,@ca,@u,@rev,0,@utc); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@s", o.SubjectRawNormalized);
        cmd.Parameters.AddWithValue("@scope", o.Scope);
        cmd.Parameters.AddWithValue("@d", o.DisplayName);
        cmd.Parameters.AddWithValue("@n", (object?)o.Note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ca", o.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@u", o.EntityId is { } id ? id.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("@rev", o.Revision);
        cmd.Parameters.AddWithValue("@utc", (object?)o.CreatedAtUtc?.ToString("o") ?? DBNull.Value);
        return (long)cmd.ExecuteScalar()!;
    }

    public List<Override> GetOverrides()
    {
        var list = new List<Override>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, subjectRawNormalized, scope, displayName, note, createdAt, entityUuid, revision, tombstone, createdAtUtc FROM overrides WHERE tombstone=0";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Override
            {
                Id = r.GetInt64(0),
                SubjectRawNormalized = r.GetString(1),
                Scope = r.GetString(2),
                DisplayName = r.GetString(3),
                Note = r.IsDBNull(4) ? null : r.GetString(4),
                CreatedAt = DateTime.Parse(r.GetString(5)),
                EntityId = r.IsDBNull(6) || string.IsNullOrEmpty(r.GetString(6)) ? null : Guid.Parse(r.GetString(6)),
                Revision = r.IsDBNull(7) ? 0 : r.GetInt64(7),
                Tombstone = !r.IsDBNull(8) && r.GetInt64(8) != 0,
                CreatedAtUtc = r.IsDBNull(9) || string.IsNullOrEmpty(r.GetString(9)) ? null : DateTimeOffset.Parse(r.GetString(9))
            });
        }
        return list;
    }

    public long InsertFriend(FriendGroup f)
    {
        if (PrivateOutbox is { Enabled: true })
            return PrivateOutbox.InTransaction(() => WriteFriend(f, insert: true, enqueue: true));
        return WriteFriend(f, insert: true, enqueue: false);
    }

    public List<FriendGroup> GetFriends()
    {
        var list = new List<FriendGroup>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, groupName, colorHex, enabled, memberNames, entityUuid, revision, tombstone FROM friends WHERE tombstone=0";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string members = r.IsDBNull(4) ? "" : r.GetString(4);
            list.Add(new FriendGroup
            {
                Id = r.GetInt64(0),
                GroupName = r.GetString(1),
                ColorHex = r.GetString(2),
                Enabled = r.GetInt32(3) != 0,
                MemberNames = members,
                EntityId = r.IsDBNull(5) || string.IsNullOrEmpty(r.GetString(5)) ? null : Guid.Parse(r.GetString(5)),
                Revision = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                Tombstone = !r.IsDBNull(7) && r.GetInt64(7) != 0
            });
        }
        return list;
    }

    public void UpdateFriend(FriendGroup f)
    {
        if (PrivateOutbox is { Enabled: true })
        {
            PrivateOutbox.InTransaction(() => WriteFriend(f, insert: false, enqueue: true));
            return;
        }
        WriteFriend(f, insert: false, enqueue: false);
    }

    public void DeleteFriend(long id)
    {
        if (PrivateOutbox is { Enabled: true })
        {
            PrivateOutbox.InTransaction(() =>
            {
                var friend = GetFriends().FirstOrDefault(x => x.Id == id);
                if (friend is null) return 0;
                EnsureFriendIdentity(friend);
                if (friend.Revision == 0)
                {
                    using var hard = _conn.CreateCommand();
                    hard.CommandText = "DELETE FROM friends WHERE id=@id";
                    hard.Parameters.AddWithValue("@id", id);
                    hard.ExecuteNonQuery();
                    PrivateOutbox.Enqueue(Guid.NewGuid(), "friend", friend.EntityId!.Value, 0, "delete", null, id);
                    return 0;
                }
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "UPDATE friends SET tombstone=1 WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                PrivateOutbox.Enqueue(Guid.NewGuid(), "friend", friend.EntityId!.Value, friend.Revision, "delete", null, id);
                return 0;
            });
            return;
        }
        using var legacy = _conn.CreateCommand();
        legacy.CommandText = "DELETE FROM friends WHERE id=@id";
        legacy.Parameters.AddWithValue("@id", id);
        legacy.ExecuteNonQuery();
    }

    private long WriteFriend(FriendGroup f, bool insert, bool enqueue)
    {
        if (insert)
        {
            if (enqueue) f.EntityId ??= Guid.NewGuid();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO friends (groupName, colorHex, enabled, memberNames, entityUuid, revision, tombstone) VALUES (@g,@c,@e,@m,@u,0,0); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@g", f.GroupName);
            cmd.Parameters.AddWithValue("@c", f.ColorHex);
            cmd.Parameters.AddWithValue("@e", f.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@m", (object?)f.MemberNames ?? "");
            cmd.Parameters.AddWithValue("@u", f.EntityId is { } uuid ? uuid.ToString("D") : DBNull.Value);
            f.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            EnsureFriendIdentity(f);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE friends SET groupName=@g, colorHex=@c, enabled=@e, memberNames=@m, entityUuid=@u WHERE id=@id";
            cmd.Parameters.AddWithValue("@g", f.GroupName);
            cmd.Parameters.AddWithValue("@c", f.ColorHex);
            cmd.Parameters.AddWithValue("@e", f.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@m", (object?)f.MemberNames ?? "");
            cmd.Parameters.AddWithValue("@u", f.EntityId!.Value.ToString("D"));
            cmd.Parameters.AddWithValue("@id", f.Id);
            cmd.ExecuteNonQuery();
        }
        if (enqueue && PrivateOutbox is { Enabled: true })
        {
            var groupId = GetGroupByName(f.GroupName)?.Id;
            PrivateOutbox.Enqueue(Guid.NewGuid(), "friend", f.EntityId!.Value, f.Revision, "upsert",
                PrivateSyncMapper.Friend(groupId, f.GroupName, f.MemberNames ?? "", f.ColorHex, f.Enabled), f.Id);
        }
        return f.Id;
    }

    private void EnsureFriendIdentity(FriendGroup f)
    {
        if (f.EntityId is { } existing && existing != Guid.Empty) return;
        if (f.Id != 0)
        {
            using var read = _conn.CreateCommand();
            read.CommandText = "SELECT entityUuid, revision FROM friends WHERE id=@id";
            read.Parameters.AddWithValue("@id", f.Id);
            using var r = read.ExecuteReader();
            if (r.Read() && !r.IsDBNull(0) && !string.IsNullOrEmpty(r.GetString(0)))
            {
                f.EntityId = Guid.Parse(r.GetString(0));
                f.Revision = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                return;
            }
        }
        f.EntityId = Guid.NewGuid();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE friends SET entityUuid=@u WHERE id=@id";
        cmd.Parameters.AddWithValue("@u", f.EntityId.Value.ToString("D"));
        cmd.Parameters.AddWithValue("@id", f.Id);
        cmd.ExecuteNonQuery();
    }

    public Group? GetGroupByName(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, url, lastFetchedAt FROM groups WHERE name=@n";
        cmd.Parameters.AddWithValue("@n", name);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Group { Id = r.GetString(0), Name = r.GetString(1), Url = r.IsDBNull(2) ? "" : r.GetString(2), LastFetchedAt = r.IsDBNull(3) ? null : DateTime.TryParse(r.GetString(3), out var dt) ? dt : null };
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}
