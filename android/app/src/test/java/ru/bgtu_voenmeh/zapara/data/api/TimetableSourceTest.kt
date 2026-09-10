package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.time.LocalDate

class TimetableSourceTest {
    @Test
    fun ensure_and_pull_use_json_when_api_configured_and_xml_flag_off() = runBlocking {
        var xml = 0
        val http = FakeHttp { call ->
            jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson())
        }
        val store = MemoryTimetableStore()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        val source = TimetableSource(api, store) { xml++ }
        source.ensure()
        assertTrue(http.requests.isNotEmpty())
        assertEquals(0, xml)
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        assertTrue(source.pull())
        assertEquals(0, xml)
        assertEquals(PIN, store.readMetadata("a")!!.meta!!.snapshotId)
    }

    @Test
    fun xml_refresh_is_refused_when_api_is_configured() {
        val store = MemoryTimetableStore().also { it.useApiCatalog = true }
        try {
            TimetableSource.guardXmlRefresh(store, store.settings())
            fail()
        } catch (e: IllegalStateException) {
            assertTrue(e.message!!.contains("API"))
        }
    }

    @Test
    fun university_xml_flag_allows_xml_and_skips_json() = runBlocking {
        var xml = 0
        val http = FakeHttp { jsonReply(catalogJson()) }
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(useUniversityXml = true, myGroupId = "a"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        val source = TimetableSource(api, store) { xml++ }
        assertTrue(source.pull())
        assertEquals(1, xml)
        assertTrue(http.requests.isEmpty())
    }

    @Test
    fun ensure_uses_bundled_snapshot_before_network() = runBlocking {
        var xml = 0
        val http = FakeHttp { error("no http") }
        val store = MemoryTimetableStore()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        val source = TimetableSource(api, store, {
            store.upsertGroup(GroupInfo("1", "A"))
            true
        }) { xml++ }
        source.ensure()
        assertEquals(0, xml)
        assertTrue(http.requests.isEmpty())
        assertEquals("A", store.groups().single().name)
    }

    @Test
    fun overlay_does_not_apply_api_metadata_after_explicit_xml_refresh() {
        val store = MemoryTimetableStore().also { it.useApiCatalog = true }
        TimetableApiCache(store).apply(
            TimetableApiSnapshot(
                TimetableApiPeriod(LocalDate.of(2026, 9, 1), 2, "API", "Europe/Moscow"),
                TimetableApiMeta(
                    "11111111-1111-4111-8111-111111111111",
                    "2026-09-08T10:00:00Z", "2026-09-08T10:01:00Z", null, "file", null, SHA, false
                ),
                TimetableApiRefresh(null, null, null, null, null, false),
                listOf(TimetableApiGroup("1", "A", 0)),
                emptyMap()
            )
        )
        val raw = ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState(
            myGroupId = "1",
            useUniversityXml = true,
            periodStart = LocalDate.of(2025, 2, 1),
            periodTitle = "XML",
            weekCount = 2,
            lastFetchedAt = "xml-stamp"
        )
        val s = overlaySettings(store, raw)
        assertEquals(LocalDate.of(2025, 2, 1), s.periodStart)
        assertEquals("XML", s.periodTitle)
        assertEquals("xml-stamp", s.lastFetchedAt)
    }
}
