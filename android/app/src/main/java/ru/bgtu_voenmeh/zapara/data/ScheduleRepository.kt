package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import androidx.room.Room
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_1_2
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_2_3
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDate

// Schedule repository: network fetch -> parse -> Room. A2 scope (no overrides/homework/friends logic yet).
class ScheduleRepository private constructor(val db: ZaparaDatabase, private val app: Context) {

    companion object {
        @Volatile
        private var instance: ScheduleRepository? = null

        /** Test hook: when false, ensureData() never touches network. Default true (production). */
        @Volatile
        var networkEnabled = true

        fun get(context: Context): ScheduleRepository {
            val app = context.applicationContext
            return instance ?: synchronized(this) {
                instance ?: ScheduleRepository(
                    Room.databaseBuilder(
                        app,
                        ZaparaDatabase::class.java,
                        "zapara.db"
                    ).addMigrations(MIGRATION_1_2, MIGRATION_2_3).build(),
                    app
                ).also { instance = it }
            }
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
        val notifyTime2: String? = "07:30"
    )

    fun settings(): SettingsState {
        val s = db.settingsDao().get() ?: return SettingsState()
        return SettingsState(
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
            notifyTime2 = s.notifyTime2
        )
    }

    fun saveSettings(s: SettingsState) {
        db.settingsDao().save(
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
                notifyTime2 = s.notifyTime2
            )
        )
    }

    fun groups(): List<GroupInfo> =
        db.groupDao().getAll().map { GroupInfo(it.id, it.name, it.url) }

    fun allForGroup(groupId: String): List<Lesson> =
        db.lessonDao().getAllForGroup(groupId).map { it.toLesson() }

    fun lessonsFor(groupId: String, date: LocalDate): List<Lesson> {
        val s = settings()
        return Schedule.lessonsForDate(allForGroup(groupId), groupId, date, s.periodStart, s.weekCount, s.parityInvert)
    }

    suspend fun ensureData(): Unit = withContext(Dispatchers.IO) {
        if (db.groupDao().getAll().isEmpty()) {
            if (!networkEnabled) throw IllegalStateException("empty db and network disabled (tests)")
            refresh()
        }
    }

    suspend fun refresh(url: String = GroupParser.DEFAULT_URL): Unit = withContext(Dispatchers.IO) {
        val xml = fetch(url)
        try { PublicExport.scheduleCache(app).writeText(xml, Charsets.UTF_8) } catch (_: Exception) {}
        val parsed = GroupParser.parse(xml, url)
        val s = settings()
        val now = java.time.OffsetDateTime.now().toString()
        db.runInTransaction {
            for (g in parsed.groups) {
                db.groupDao().upsert(GroupEntity(g.id, g.name, g.url))
            }
            val byGroup = parsed.lessons.groupBy { it.groupId }
            for ((gid, list) in byGroup) {
                db.lessonDao().clearForGroup(gid)
                db.lessonDao().insertAll(list.map { it.toEntity() })
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
        try { PublicExport.ensure(app) } catch (_: Exception) {}
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
