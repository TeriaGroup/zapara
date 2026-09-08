package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.net.URI

class ApiRefreshCoordinatorTest {
    @Test
    fun adoption_catalog_then_selection_loads_typed_cache() = runBlocking {
        val http = FakeHttp { call ->
            jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson())
        }
        val store = MemoryTimetableStore()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        assertTrue(api.configured)
        assertTrue(api.refresh())
        assertNull(store.settings().lastFetchedAt)
        assertNull(store.readMetadata("a"))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        assertTrue(api.refresh(neededOnly = true))
        assertEquals(PIN, store.readMetadata("a")!!.meta!!.snapshotId)
        assertEquals("2026-09-01", store.settings().periodStart.toString())
    }

    @Test
    fun bad_configuration_is_explicit_not_legacy() {
        val work = ProfileWork()
        for (url in listOf("not a url", "http://example.invalid/", "   ")) {
            val api = ApiRefreshCoordinator(MemoryTimetableStore(), work, url, FakeHttp { jsonReply("{}") })
            assertTrue(api.configured)
            assertTrue(!api.configurationError.isNullOrBlank())
            val leaked = url.trim()
            if (leaked.isNotEmpty()) assertTrue(!api.configurationError!!.contains(leaked))
        }
    }

    @Test
    fun unconfigured_keeps_xml_path() {
        val api = ApiRefreshCoordinator(MemoryTimetableStore(), ProfileWork(), null, FakeHttp { jsonReply("{}") })
        assertFalse(api.configured)
        assertNull(api.configurationError)
    }

    @Test
    fun university_xml_setting_skips_json_client() = runBlocking {
        val http = FakeHttp { jsonReply(catalogJson()) }
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(useUniversityXml = true, myGroupId = "a"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        assertFalse(api.refresh())
        assertTrue(http.requests.isEmpty())
    }

    @Test
    fun last_good_retained_on_failure() = runBlocking {
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        var fail = false
        val http = FakeHttp { call ->
            if (fail) throw java.io.IOException("down")
            jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson())
        }
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        assertTrue(api.refresh())
        val before = store.dump()
        fail = true
        assertFalse(api.refresh())
        assertEquals(before, store.dump())
        assertEquals("Не удалось обновить расписание API. Локальные данные сохранены.", api.lastError)
    }

    @Test
    fun friend_groups_are_fetched_with_selected() = runBlocking {
        val http = FakeHttp { call ->
            jsonReply(
                if (call.url.substringBefore('?').endsWith("/groups")) catalogJson(PIN, "a", "b")
                else if (call.url.contains("/b/")) scheduleJson("b")
                else scheduleJson("a")
            )
        }
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        store.friends += Friend("ТЕСТ-ГРУППА", "#FF4CC38A", true, "Иван")
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        assertTrue(api.refresh())
        assertNotNull(store.readMetadata("a"))
        val paths = http.requests.map { URI(it.url).rawPath }
        assertTrue(paths.any { it.endsWith("/groups") })
        assertTrue(http.requests.any { it.url.contains("timetable") })
    }

    @Test
    fun late_response_after_stop_never_commits() = runBlocking {
        val arrived = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val http = FakeHttp { _ ->
            arrived.complete(Unit)
            release.await()
            jsonReply(catalogJson())
        }
        val store = MemoryTimetableStore()
        val work = ProfileWork()
        val api = ApiRefreshCoordinator(store, work, "https://example.invalid/", http)
        val before = store.dump()
        val task = async { api.refresh() }
        arrived.await()
        api.stop()
        release.complete(Unit)
        assertFalse(task.await())
        assertEquals(before, store.dump())
    }
}
