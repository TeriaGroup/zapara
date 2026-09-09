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
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase


@RunWith(AndroidJUnit4::class)
class ShellNavigationTest {

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
            val mine = mutableListOf<LessonEntity>()
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
            }
            db.lessonDao().insertAll(mine)
            db.settingsDao().save(SettingsEntity(myGroupId = "3313"))
            db.friendDao().insert(FriendEntity(groupName = "09С31", colorHex = "#FF6CA5E0", enabled = true))
            db.homeworkDao().insert(
                HomeworkEntity(
                    subjectRawNormalized = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ"),
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
                try { seed(db) } finally { db.close() }
            } catch (e: Throwable) { failure = e }
        }
        t.start()
        t.join(60_000)
        failure?.let { throw RuntimeException("seed failed", it) }
    }

    private fun treeTexts(): List<String> = ui.treeTexts()

    private fun reload() {
        ui.reloadGuest()
    }

    private fun capture(name: String) {
        var activity: MainActivity? = null
        ui.onActivity { activity = it }
        val file = Frames.capture(requireNotNull(activity), name)
        val instrumentation = androidx.test.platform.app.InstrumentationRegistry.getInstrumentation()
        instrumentation.uiAutomation.executeShellCommand("mkdir -p /data/local/tmp/zapara-shell-task2").close()
        instrumentation.uiAutomation.executeShellCommand("cp ${file.absolutePath} /data/local/tmp/zapara-shell-task2/${file.name}").close()
    }

    @Test
    fun bar_sections_group_and_back() {
        reload()
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Расписание") } }
        capture("shell-schedule-dark")
        ui.clickRes("Nav.Maps")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Карты") } }
        capture("shell-maps-dark")
        ui.clickRes("Nav.Homework")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Домашка") } }
        capture("shell-homework-dark")
        ui.clickRes("Nav.Sections")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Неделя") } }
        capture("sheet-sections-dark")
        ui.clickText("Неделя")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Неделя") } }
        capture("shell-week-dark")
        ui.clickRes("Nav.Sections")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Сводка") } }
        ui.clickText("Сводка")
        capture("shell-summary-dark")
        ui.clickRes("Nav.Sections")
        ui.clickText("Преподаватели")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Преподаватели") } }
        capture("shell-teachers-dark")
        ui.clickRes("Nav.Sections")
        ui.clickText("Друзья")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Максимум пять групп") } }
        capture("shell-friends-dark")
        ui.clickRes("Nav.Sections")
        ui.clickText("Настройки")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Настройки") } }
        capture("shell-settings-dark")
        ui.clickRes("Top.GroupChip")
        ui.waitUntil(20_000) { treeTexts().any { it.contains("Поиск группы") || it.contains("Выбрать группу") } }
        capture("picker-group-dark")
        ui.onActivity {
            ui.shellViewModel(it).onEvent(ru.bgtu_voenmeh.zapara.ui.shell.ShellEvent.Overlay(ru.bgtu_voenmeh.zapara.ui.shell.ShellOverlay.None))
        }
    }
}
