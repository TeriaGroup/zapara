package ru.bgtu_voenmeh.zapara

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository

@RunWith(AndroidJUnit4::class)
class VoenmehLiveAppRefreshTest {
    @get:Rule val ui = NativeUiRule()

    @Test fun settings_refresh_loads_live_university_json() {
        val before = io {
            val repo = repo()
            val group = repo.groups().firstOrNull { it.name == "А863С" }
            check(group != null) { "В каталоге нет А863С, групп: ${repo.groups().size}" }
            repo.saveSettings(repo.settings().copy(myGroupId = group.id))
            repo.settings().lastFetchedAt
        }
        ui.reloadGuest()
        ui.clickRes("Nav.Sections")
        ui.clickRes("Sections.Settings")
        ui.clickRes("Settings.Refresh")
        ui.waitUntil(45_000) {
            val texts = ui.treeTexts()
            check(texts.none { it.contains("Не удалось") }) { "Refresh failed: $texts" }
            io { repo().settings().lastFetchedAt } != before
        }
        io {
            val repo = repo()
            val id = repo.settings().myGroupId
            check(!id.isNullOrBlank()) { "группа не выбрана" }
            val lessons = repo.allForGroup(id!!)
            assertTrue("ожидали живые пары, получили ${lessons.size}", lessons.size >= 8)
            assertTrue(
                lessons.any { it.subjectRaw.contains("ФИЗИКА") || it.subjectRaw.contains("ОСН") }
            )
            assertTrue(lessons.any { it.buildingRaw == "ГК" || it.buildingRaw == "УЛК" })
            assertTrue(repo.settings().periodTitle.orEmpty().contains("2026"))
        }
        ui.pressBack()
        ui.clickRes("Nav.Schedule")
        ui.waitUntil(10_000) {
            ui.treeTexts().any { it.contains("А863С") }
        }
    }

    private fun repo(): ScheduleRepository =
        ApplicationProvider.getApplicationContext<ZaparaApplication>().container.repo

    private fun <T> io(block: () -> T): T = runBlocking(Dispatchers.IO) { block() }
}
