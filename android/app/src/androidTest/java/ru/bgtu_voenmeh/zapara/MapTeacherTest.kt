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
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_1_2
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase


// A4: offline maps (bundled assets) + teacher finder (bundled lecturer XML).
@RunWith(AndroidJUnit4::class)
class MapTeacherTest {

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
            val lessons = mutableListOf<LessonEntity>()
            for (dow in 1..6) {
                lessons.add(
                    LessonEntity(
                        groupId = "3313", dayOfWeek = dow, parity = 0, idx = 1,
                        timeStart = "09:00", timeEnd = "10:35",
                        subjectRaw = "лек ВЫСШ. МАТЕМАТ",
                        subjectNormalized = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ"),
                        teacherRaw = "Барт Е.Л.", roomRaw = "493", buildingRaw = "ГК",
                        typeRaw = "лек", classroomRaw = "493;"
                    )
                )
                lessons.add(
                    LessonEntity(
                        groupId = "3313", dayOfWeek = dow, parity = 0, idx = 2,
                        timeStart = "10:50", timeEnd = "12:25",
                        subjectRaw = "лек ИСТОРИЯ",
                        subjectNormalized = Parity.normalizeSubject("лек ИСТОРИЯ"),
                        teacherRaw = "Попова В.В.", roomRaw = "526", buildingRaw = "УЛК",
                        typeRaw = "лек", classroomRaw = "526*;"
                    )
                )
            }
            db.lessonDao().insertAll(lessons)
            db.settingsDao().save(SettingsEntity(myGroupId = "3313"))
            db.friendDao().insert(
                FriendEntity(groupName = "09С31", colorHex = "#FF6CA5E0", enabled = true)
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

    private fun reload() {
        ui.reloadGuest()
    }

    @Test
    fun mapOpensOfflineFromRowButton() {
        reload()
        ui.waitUntil(20_000) {
            val texts = treeTexts()
            texts.any { it.contains("09:00") } || texts.any { it.contains("Карты") }
        }
        try {
            ui.clickRes("Lesson.Room.1")
        } catch (_: Throwable) {
            ui.clickRes("Nav.Maps")
        }
        ui.waitUntil(20_000) {
            val texts = treeTexts()
            texts.any { it.contains("Карты") } && (texts.any { it.contains("493") } || texts.any { it.contains("ГК") } || texts.any { it.contains("этаж") })
        }
    }

    @Test
    fun teacherFinderListsBundledLecturers() {
        reload()
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Расписание") } }
        ui.clickRes("Nav.Sections")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Преподаватели") } }
        ui.clickText("Преподаватели")
        ui.waitUntil(30_000) { treeTexts().any { it.contains("Барт") } }
    }
}
