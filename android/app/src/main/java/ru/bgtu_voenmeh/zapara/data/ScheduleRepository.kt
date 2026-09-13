package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import androidx.room.Room
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.db.FriendDao
import ru.bgtu_voenmeh.zapara.data.db.FriendEntity
import ru.bgtu_voenmeh.zapara.data.db.GroupDao
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.api.RoomTimetableStore
import ru.bgtu_voenmeh.zapara.data.api.TimetableStore
import ru.bgtu_voenmeh.zapara.data.api.overlaySettings
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_1_2
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_2_3
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_3_4
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_4_5
import ru.bgtu_voenmeh.zapara.data.db.SettingsDao
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.api.TimetableSource
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import ru.bgtu_voenmeh.zapara.data.sync.FriendValue
import ru.bgtu_voenmeh.zapara.data.sync.RoomSyncOutbox
import ru.bgtu_voenmeh.zapara.data.sync.SettingsValue
import ru.bgtu_voenmeh.zapara.data.sync.SyncLocalIdentity
import ru.bgtu_voenmeh.zapara.data.sync.SyncValidation
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDate
import java.util.UUID

class ScheduleRepository private constructor(
    private val dbRef: ZaparaDatabase?,
    val store: TimetableStore,
    val work: ProfileWork?,
    var outbox: RoomSyncOutbox?,
    private val friendDao: FriendDao,
    private val settingsDao: SettingsDao,
    private val groupDao: GroupDao
) {
    val db: ZaparaDatabase get() = dbRef ?: error("Профиль не открыт")

    constructor(
        db: ZaparaDatabase,
        store: TimetableStore = RoomTimetableStore(db),
        work: ProfileWork? = null,
        outbox: RoomSyncOutbox? = null
    ) : this(db, store, work, outbox, db.friendDao(), db.settingsDao(), db.groupDao())

    internal constructor(
        store: TimetableStore,
        friendDao: FriendDao,
        settingsDao: SettingsDao,
        groupDao: GroupDao,
        outbox: RoomSyncOutbox?,
        work: ProfileWork? = null
    ) : this(null, store, work, outbox, friendDao, settingsDao, groupDao)

    companion object {
        @Volatile
        private var attached: ScheduleRepository? = null

        /** Test hook: when false, ensureData() never touches network. Default true (production). */
        @Volatile
        var networkEnabled = true

        fun attach(repo: ScheduleRepository) {
            attached = repo
        }

        fun detach(repo: ScheduleRepository) {
            if (attached === repo) attached = null
        }

        /** Current profile repository. Does not own a singleton database. */
        fun get(context: Context): ScheduleRepository {
            attached?.let { return it }
            val app = context.applicationContext
            if (app is ru.bgtu_voenmeh.zapara.ZaparaApplication) return app.container.repo
            error("Профиль не открыт")
        }

        fun openDatabase(context: Context, profile: ProfileDescriptor): ZaparaDatabase {
            val name = profile.databaseName
            val builder = if (name.contains('/') || name.contains('\\')) {
                val file = File(context.applicationContext.getDatabasePath("zapara.db").parentFile, name)
                file.parentFile?.mkdirs()
                Room.databaseBuilder(context.applicationContext, ZaparaDatabase::class.java, file.absolutePath)
            } else {
                Room.databaseBuilder(context.applicationContext, ZaparaDatabase::class.java, name)
            }
            return builder.addMigrations(MIGRATION_1_2, MIGRATION_2_3, MIGRATION_3_4, MIGRATION_4_5).build()
        }

        private val friendPalette = listOf("#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C")

        internal fun friendPaletteIndex(hex: String?): Int {
            if (hex.isNullOrBlank()) return 1
            var h = hex.trim()
            if (h.length == 9 && h[0] == '#') h = "#" + h.substring(3)
            val i = friendPalette.indexOfFirst { it.equals(h, ignoreCase = true) }
            return if (i >= 0) i + 1 else 1
        }
    }

    data class SettingsState(
        val myGroupId: String? = null,
        val parityInvert: Boolean = false,
        val language: String = "ru",
        val periodStart: LocalDate = LocalDate.of(2026, 9, 1),
        val weekCount: Int = 2,
        val periodTitle: String? = null,
        val lastFetchedAt: String? = null,
        val intersectionStrictness: Int = 25,
        val alwaysShowAllTrafficLights: Boolean = false,
        val notifyEnabled: Boolean = true,
        val notifyTime1: String? = "20:00",
        val notifyTime2: String? = "07:30",
        val theme: String = "system",
        val animations: Boolean = true,
        val useUniversityXml: Boolean = false
    )

    fun settings(): SettingsState {
        val s = settingsDao.get() ?: return overlaySettings(store, SettingsState())
        return overlaySettings(store, entityToState(s))
    }

    fun saveSettings(s: SettingsState) {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
            val box = outbox
            if (box?.enabled == true && settingsSyncChanged(s)) {
                box.inTransaction {
                    writeSettings(s)
                    val ident = ensureSettingsIdentity()
                    box.enqueue(
                        UUID.randomUUID(),
                        "settings",
                        SyncValidation.SETTINGS_ID,
                        ident.revision,
                        "upsert",
                        settingsValue(s),
                        1
                    )
                    0
                }
            } else {
                writeSettings(s)
            }
        } finally {
            ticket?.close()
        }
    }

    fun friends(): List<FriendEntity> = friendDao.getAll()

    fun insertFriend(friend: FriendEntity): Long {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
            val box = outbox
            return if (box?.enabled == true) {
                box.inTransaction { writeFriend(friend, insert = true, enqueue = true) }
            } else {
                writeFriend(friend, insert = true, enqueue = false)
            }
        } finally {
            ticket?.close()
        }
    }

    fun updateFriend(friend: FriendEntity) {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
            val box = outbox
            if (box?.enabled == true) {
                box.inTransaction { writeFriend(friend, insert = false, enqueue = true); 0 }
            } else {
                writeFriend(friend, insert = false, enqueue = false)
            }
        } finally {
            ticket?.close()
        }
    }

    fun deleteFriend(id: Long) {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
            val box = outbox
            if (box?.enabled == true) {
                box.inTransaction { deleteFriendCore(id, enqueue = true); 0 }
            } else {
                deleteFriendCore(id, enqueue = false)
            }
        } finally {
            ticket?.close()
        }
    }

    private fun writeSettings(s: SettingsState) {
        settingsDao.save(
            SettingsEntity(
                myGroupId = s.myGroupId,
                parityInvert = s.parityInvert,
                language = s.language,
                periodStart = s.periodStart.toString(),
                weekCount = s.weekCount,
                periodTitle = s.periodTitle,
                lastFetchedAt = s.lastFetchedAt,
                intersectionStrictness = s.intersectionStrictness,
                alwaysShowAllTrafficLights = s.alwaysShowAllTrafficLights,
                notifyEnabled = s.notifyEnabled,
                notifyTime1 = s.notifyTime1,
                notifyTime2 = s.notifyTime2,
                theme = s.theme,
                animations = s.animations,
                useUniversityXml = s.useUniversityXml
            )
        )
    }

    private fun settingsSyncChanged(s: SettingsState): Boolean {
        val cur = settingsDao.get()?.let { entityToState(it) } ?: SettingsState()
        return cur.myGroupId != s.myGroupId ||
            cur.parityInvert != s.parityInvert ||
            cur.notifyTime1 != s.notifyTime1 ||
            cur.notifyTime2 != s.notifyTime2 ||
            cur.intersectionStrictness != s.intersectionStrictness ||
            cur.alwaysShowAllTrafficLights != s.alwaysShowAllTrafficLights
    }

    private fun entityToState(s: SettingsEntity) = SettingsState(
        myGroupId = s.myGroupId,
        parityInvert = s.parityInvert,
        language = s.language,
        periodStart = runCatching { LocalDate.parse(s.periodStart) }.getOrNull()
            ?: LocalDate.of(2026, 9, 1),
        weekCount = if (s.weekCount > 0) s.weekCount else 2,
        periodTitle = s.periodTitle,
        lastFetchedAt = s.lastFetchedAt,
        intersectionStrictness = s.intersectionStrictness,
        alwaysShowAllTrafficLights = s.alwaysShowAllTrafficLights,
        notifyEnabled = s.notifyEnabled,
        notifyTime1 = s.notifyTime1,
        notifyTime2 = s.notifyTime2,
        theme = s.theme,
        animations = s.animations,
        useUniversityXml = s.useUniversityXml
    )

    private fun settingsValue(s: SettingsState) = SettingsValue(
        s.myGroupId,
        s.parityInvert,
        s.notifyTime1,
        s.notifyTime2,
        s.intersectionStrictness,
        s.alwaysShowAllTrafficLights
    )

    private fun ensureSettingsIdentity(): SyncLocalIdentity {
        val box = outbox!!
        box.identity("settings", 1)?.let { return it }
        return box.remember("settings", 1, SyncValidation.SETTINGS_ID, 0, box.nowUtc(), null)
    }

    private fun writeFriend(friend: FriendEntity, insert: Boolean, enqueue: Boolean): Long {
        val stored = if (insert) {
            friendDao.insert(friend)
        } else {
            val existing = friendDao.getAll().firstOrNull { it.id == friend.id } ?: return 0
            friendDao.update(friend.copy(id = existing.id))
            existing.id
        }
        if (enqueue) {
            val row = friendDao.getAll().first { it.id == stored }
            val ident = if (insert) {
                val entityId = UUID.randomUUID()
                outbox!!.remember("friend", stored, entityId, 0, outbox!!.nowUtc(), null)
            } else {
                ensureFriendIdentity(stored)
            }
            outbox!!.enqueue(
                UUID.randomUUID(),
                "friend",
                ident.entityId,
                ident.revision,
                "upsert",
                friendValue(row),
                stored
            )
        }
        return stored
    }

    private fun deleteFriendCore(id: Long, enqueue: Boolean) {
        if (!enqueue) {
            friendDao.delete(id)
            return
        }
        val existing = friendDao.getAll().firstOrNull { it.id == id } ?: return
        val ident = ensureFriendIdentity(existing.id)
        friendDao.delete(id)
        outbox!!.enqueue(UUID.randomUUID(), "friend", ident.entityId, ident.revision, "delete", null, id)
    }

    private fun ensureFriendIdentity(id: Long): SyncLocalIdentity {
        outbox!!.identity("friend", id)?.let { return it }
        return outbox!!.remember("friend", id, UUID.randomUUID(), 0, outbox!!.nowUtc(), null)
    }

    private fun friendValue(friend: FriendEntity) = FriendValue(
        groupDao.getAll().firstOrNull { it.name == friend.groupName }?.id,
        friend.groupName,
        friend.memberNames,
        friendPaletteIndex(friend.colorHex),
        friend.enabled
    )

    fun groups(): List<GroupInfo> = store.groups()

    fun allForGroup(groupId: String): List<Lesson> =
        db.lessonDao().getAllForGroup(groupId).map { it.toLesson() }

    fun lessonsFor(groupId: String, date: LocalDate): List<Lesson> {
        val s = settings()
        return Schedule.lessonsForDate(allForGroup(groupId), groupId, date, s.periodStart, s.weekCount, s.parityInvert)
    }

    suspend fun ensureData(): Unit = withContext(Dispatchers.IO) {
        if (db.groupDao().getAll().isEmpty()) {
            if (!networkEnabled) throw IllegalStateException("empty db and network disabled (tests)")
            TimetableSource.guardXmlRefresh(store, settings())
            refresh()
        }
    }

    suspend fun applyBundled(context: Context): Boolean = withContext(Dispatchers.IO) {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
            context.assets.open("TimetableGroup50.xml").use { stream ->
                ingestParsed(GroupParser.parse(stream), "asset:TimetableGroup50.xml")
            }
            true
        } catch (e: CancellationException) {
            throw e
        } catch (t: Throwable) {
            android.util.Log.e("ZaparaExport", "bundled", t)
            false
        } finally {
            ticket?.close()
        }
    }

    suspend fun refresh(url: String = GroupParser.DEFAULT_URL): Unit = withContext(Dispatchers.IO) {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
            TimetableSource.guardXmlRefresh(store, settings())
            ingestParsed(GroupParser.parse(fetch(url)), url)
        } finally {
            ticket?.close()
        }
    }

    private fun ingestParsed(parsed: ParsedSchedule, url: String) {
        val s = settings()
        val now = java.time.OffsetDateTime.now().toString()
        db.runInTransaction {
            for (g in parsed.groups) {
                db.groupDao().upsert(GroupEntity(g.id, g.name, g.url))
            }
            val byGroup = parsed.lessons.groupBy { it.groupId }
            for ((gid, list) in byGroup) {
                db.lessonDao().clearForGroup(gid)
                list.map { it.toEntity() }.chunked(200).forEach { chunk ->
                    db.lessonDao().insertAll(chunk)
                }
            }
            saveSettings(
                s.copy(
                    periodStart = parsed.periodStart,
                    weekCount = parsed.weekCount,
                    periodTitle = parsed.periodTitle,
                    lastFetchedAt = now
                )
            )
        }
    }

    private fun fetch(url: String): String {
        val conn = URL(url).openConnection() as HttpURLConnection
        try {
            conn.setRequestProperty("User-Agent", "Mozilla/5.0 (Linux; Android) Zapara/1.0")
            conn.connectTimeout = 20_000
            conn.readTimeout = 30_000
            conn.connect()
            if (conn.responseCode != HttpURLConnection.HTTP_OK) {
                throw IllegalStateException("HTTP ${conn.responseCode}")
            }
            return conn.inputStream.bufferedReader(Charsets.UTF_8).readText()
        } finally {
            conn.disconnect()
        }
    }

    private fun Lesson.toEntity() = LessonEntity(
        groupId = groupId, dayOfWeek = dayOfWeek, parity = parity, idx = index,
        timeStart = timeStart, timeEnd = timeEnd, subjectRaw = subjectRaw,
        subjectNormalized = subjectNormalized, teacherRaw = teacherRaw,
        roomRaw = roomRaw, buildingRaw = buildingRaw, typeRaw = typeRaw,
        classroomRaw = classroomRaw
    )

    private fun LessonEntity.toLesson() = Lesson(
        groupId = groupId, dayOfWeek = dayOfWeek, parity = parity, index = idx,
        timeStart = timeStart, timeEnd = timeEnd, subjectRaw = subjectRaw,
        subjectNormalized = subjectNormalized, teacherRaw = teacherRaw,
        roomRaw = roomRaw, buildingRaw = buildingRaw, typeRaw = typeRaw,
        classroomRaw = classroomRaw
    )
}
