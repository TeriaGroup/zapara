package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import androidx.room.Room
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.api.RoomTimetableStore
import ru.bgtu_voenmeh.zapara.data.api.TimetableStore
import ru.bgtu_voenmeh.zapara.data.api.overlaySettings
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_1_2
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_2_3
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_3_4
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_4_5
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.api.TimetableSource
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.time.LocalDate
import java.time.LocalTime

class ScheduleRepository(
    val db: ZaparaDatabase,
    val store: TimetableStore = RoomTimetableStore(db),
    val work: ProfileWork? = null
) {

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
        val s = db.settingsDao().get() ?: return overlaySettings(store, SettingsState())
        return overlaySettings(
            store,
            SettingsState(
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
        )
    }

    fun saveSettings(s: SettingsState) {
        val ticket = work?.enter()
        try {
            ticket?.throwIfStale()
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
                    notifyTime2 = s.notifyTime2,
                    theme = s.theme,
                    animations = s.animations,
                    useUniversityXml = s.useUniversityXml
                )
            )
        } finally {
            ticket?.close()
        }
    }

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
        } catch (_: Throwable) {
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
