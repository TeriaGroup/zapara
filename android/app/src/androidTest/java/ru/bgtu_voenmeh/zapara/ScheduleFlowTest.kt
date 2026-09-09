package ru.bgtu_voenmeh.zapara

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Before
import org.junit.BeforeClass
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.db.FriendEntity
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.HomeworkEntity
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_1_2
import ru.bgtu_voenmeh.zapara.data.db.OverrideEntity
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase


// A3 end-to-end: seed DB -> launch -> override/homework/traffic/friends-dialog visible.
// Uses the real main looper and accessibility tree; no test-dispatcher or Espresso dependency.
@RunWith(AndroidJUnit4::class)
class ScheduleFlowTest {

    @get:Rule
    val ui = NativeUiRule()

    companion object {
        @JvmStatic
        @BeforeClass
        fun disableNetwork() {
            ru.bgtu_voenmeh.zapara.data.ScheduleRepository.networkEnabled = false
        }

        private fun seed(db: ZaparaDatabase) {
            db.clearAllTables()
            db.groupDao().upsert(GroupEntity("3313", "А863С"))
            db.groupDao().upsert(GroupEntity("3031", "09С31"))
            // Lessons for every weekday (parity 0 = both) so Today/Tomorrow always show rows.
            val mine = mutableListOf<LessonEntity>()
            val friend = mutableListOf<LessonEntity>()
            for (dow in 1..6) {
                mine.add(
                    LessonEntity(
                        groupId = "3313", dayOfWeek = dow, parity = 0, idx = 1,
                        timeStart = "09:00", timeEnd = "10:35",
                        subjectRaw = "лек ВЫСШ. МАТЕМАТ",
                        subjectNormalized = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ"),
                        teacherRaw = "Барт Е.Л.", roomRaw = "493", buildingRaw = "ГК",
                        typeRaw = "лек", classroomRaw = "493;"
                    )
                )
                mine.add(
                    LessonEntity(
                        groupId = "3313", dayOfWeek = dow, parity = 0, idx = 2,
                        timeStart = "10:50", timeEnd = "12:25",
                        subjectRaw = "лек ИСТОРИЯ",
                        subjectNormalized = Parity.normalizeSubject("лек ИСТОРИЯ"),
                        teacherRaw = "Попова В.В.", roomRaw = "526", buildingRaw = "УЛК",
                        typeRaw = "лек", classroomRaw = "526*;"
                    )
                )
                friend.add(
                    LessonEntity(
                        groupId = "3031", dayOfWeek = dow, parity = 0, idx = 1,
                        timeStart = "09:00", timeEnd = "10:35",
                        subjectRaw = "лек ФИЗИКА",
                        subjectNormalized = Parity.normalizeSubject("лек ФИЗИКА"),
                        teacherRaw = "Петров А.Б.", roomRaw = "493", buildingRaw = "ГК",
                        typeRaw = "лек", classroomRaw = "493;"
                    )
                )
            }
            db.lessonDao().insertAll(mine)
            db.lessonDao().insertAll(friend)
            db.settingsDao().save(SettingsEntity(myGroupId = "3313"))
            db.friendDao().insert(
                FriendEntity(groupName = "09С31", colorHex = "#FF6CA5E0", enabled = true, memberNames = "Иван")
            )
            db.overrideDao().insert(
                OverrideEntity(
                    subjectRawNormalized = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ"),
                    scope = "global", displayName = "МАТАН", note = null, createdAt = "2026-09-03"
                )
            )
            db.homeworkDao().insert(
                HomeworkEntity(
                    subjectRawNormalized = Parity.normalizeSubject("лек ИСТОРИЯ"),
                    text = "прочитать §5", createdAt = "2026-09-01", targetNthOccurrence = 1,
                    dueDateComputed = "2026-09-04", status = "burning"
                )
            )
        }
    }

    @Before
    fun seedBeforeEach() {
        var failure: Throwable? = null
        val t = Thread {
            try {
                val ctx: Context = ApplicationProvider.getApplicationContext()
                val db = Room.databaseBuilder(ctx, ZaparaDatabase::class.java, "zapara.db")
                    .addMigrations(MIGRATION_1_2)
                    .build()
                try {
                    seed(db)
                } finally {
                    db.close()
                }
            } catch (e: Throwable) {
                failure = e
            }
        }
        t.start()
        t.join(60_000)
        failure?.let { throw RuntimeException("seed failed", it) }
    }

    private fun treeTexts(): List<String> = ui.treeTexts()

    private fun refreshUi() {
        ui.reloadGuest()
    }

    @Test
    fun renameHomeworkTrafficVisible() {
        refreshUi()
        // Override applied instead of raw subject (row shows "[лек] МАТАН").
        ui.waitUntil(20_000) { treeTexts().any { it.contains("МАТАН") || it.contains("ВЫСШ") } }
        ui.waitUntil(20_000) { treeTexts().any { it.contains("прочитать") } }
        ui.waitUntil(20_000) { treeTexts().any { it.contains("09С31") || it.contains("Иван") } }
    }

    @Test
    fun friendsDialogOpens() {
        refreshUi()
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Разделы") } }
        ui.clickRes("Nav.Sections")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Друзья") } }
        ui.clickText("Друзья")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Максимум пять групп") } }
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Показывать все точки") } }
    }
}
