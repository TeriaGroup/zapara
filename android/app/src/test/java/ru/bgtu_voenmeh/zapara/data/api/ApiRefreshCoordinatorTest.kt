package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ParsedSchedule
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.net.URI
import java.time.LocalDate
import java.time.OffsetDateTime

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
    fun stale_api_snapshot_without_direct_source_is_not_reported_as_success() = runBlocking {
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        val http = FakeHttp { call ->
            val body = if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson()
            jsonReply(body.replace("\"stale\":false", "\"stale\":true"))
        }
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)

        assertFalse(api.refresh())
        assertNull(store.readMetadata("a"))
        assertTrue(api.lastError?.isNotBlank() == true)
    }

    @Test
    fun stale_api_uses_direct_source_with_existing_group_id_and_fresh_stamp() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "old")))
        store.homeworkText = "keep homework"
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { call ->
                val body = if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson()
                jsonReply(body.replace("\"stale\":false", "\"stale\":true"))
            },
            universityLoader = { names -> directSchedule(names) },
            clock = { OffsetDateTime.parse("2026-10-02T12:00:00+03:00") })

        assertTrue(api.refresh())
        assertEquals("a", store.settings().myGroupId)
        assertEquals("fresh", store.allLessons("a").single().subjectRaw)
        assertEquals("keep homework", store.homeworkText)
        assertEquals("university", store.readMetadata("a")?.source)
        assertEquals("2026-10-02T12:00+03:00", store.settings().lastFetchedAt)
        assertEquals("university", store.readMetadata("")?.source)
    }

    @Test
    fun failed_api_uses_direct_source_but_missing_required_group_keeps_last_good() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "old")))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        val before = store.dump()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") },
            universityLoader = { directSchedule(listOf("ДРУГАЯ-ГРУППА")) })

        assertFalse(api.refresh())
        assertEquals(before, store.dump())
    }

    @Test
    fun failed_api_with_older_direct_period_keeps_last_good() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.saveSettings(store.settings().copy(myGroupId = "a", lastFetchedAt = "2026-10-01T00:00:00Z",
            periodStart = LocalDate.of(2026, 9, 1)))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") },
            universityLoader = { directSchedule(it).copy(periodStart = LocalDate.of(2026, 2, 1)) })
        val before = store.dump()

        assertFalse(api.refresh())
        assertEquals(before, store.dump())
    }

    @Test
    fun direct_source_can_clear_a_required_group_that_is_truly_empty() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("empty", "ПУСТАЯ"))
        store.replaceLessons("empty", listOf(Lesson(groupId = "empty", subjectRaw = "old")))
        store.saveSettings(store.settings().copy(myGroupId = "empty"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") },
            universityLoader = { names -> directSchedule(names).copy(lessons = emptyList()) })

        assertTrue(api.refresh())
        assertTrue(store.allLessons("empty").isEmpty())
        assertEquals("university", store.readMetadata("empty")?.source)
    }

    @Test
    fun changed_selection_during_direct_download_keeps_old_cache() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "old")))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") },
            universityLoader = { names ->
                store.saveSettings(store.settings().copy(myGroupId = "other"))
                directSchedule(names)
            })

        assertFalse(api.refresh())
        assertEquals("other", store.settings().myGroupId)
        assertEquals("old", store.allLessons("a").single().subjectRaw)
        assertNull(store.readMetadata("a"))
    }

    @Test
    fun stale_ensure_group_does_not_replace_fresher_direct_lessons() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "fresh direct")))
        val direct = directSchedule(listOf("ТЕСТ-ГРУППА")).copy(
            lessons = listOf(Lesson(groupId = "ТЕСТ-ГРУППА", subjectRaw = "fresh direct")))
        TimetableApiCache(store).applyUniversity(direct, listOf("ТЕСТ-ГРУППА"), store.settings(),
            emptyList(), "2026-10-02T12:00+03:00") {}
        val before = store.dump()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { call ->
                val body = if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson()
                jsonReply(body.replace("\"stale\":false", "\"stale\":true"))
            })

        assertFalse(api.ensureGroup("ТЕСТ-ГРУППА"))
        assertEquals(before, store.dump())
    }

    @Test
    fun healthy_but_older_ensure_group_does_not_replace_fresher_direct_lessons() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "fresh direct")))
        val direct = directSchedule(listOf("ТЕСТ-ГРУППА")).copy(
            lessons = listOf(Lesson(groupId = "ТЕСТ-ГРУППА", subjectRaw = "fresh direct")))
        TimetableApiCache(store).applyUniversity(direct, listOf("ТЕСТ-ГРУППА"), store.settings(),
            emptyList(), "2026-10-02T12:00+03:00") {}
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { call ->
                jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson())
            })
        val before = store.dump()

        assertFalse(api.ensureGroup("ТЕСТ-ГРУППА"))
        assertEquals(before, store.dump())
        assertEquals("fresh direct", store.allLessons("a").single().subjectRaw)
    }

    @Test
    fun direct_source_updates_selected_id_when_legacy_and_api_ids_share_a_name() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("selected-old", "ТЕСТ-ГРУППА"))
        store.upsertGroup(GroupInfo("api-duplicate", "ТЕСТ-ГРУППА"))
        store.insertCatalog("api-duplicate", "ТЕСТ-ГРУППА")
        store.replaceLessons("selected-old", listOf(Lesson(groupId = "selected-old", subjectRaw = "old")))
        store.homeworkText = "keep homework"
        store.saveSettings(store.settings().copy(myGroupId = "selected-old"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") },
            universityLoader = { names -> directSchedule(names) },
            clock = { OffsetDateTime.parse("2026-10-02T12:00:00+03:00") })

        assertTrue(api.refresh())
        assertEquals("selected-old", store.settings().myGroupId)
        assertEquals("fresh", store.allLessons("selected-old").single().subjectRaw)
        assertEquals("2026-10-02T12:00+03:00", store.readMetadata("selected-old")?.fetchedAt)
        assertEquals("keep homework", store.homeworkText)
    }

    @Test
    fun second_required_group_write_failure_rolls_back_direct_update() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "МОЯ-ГРУППА"))
        store.upsertGroup(GroupInfo("b", "ДРУЖЕСКАЯ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "old mine")))
        store.replaceLessons("b", listOf(Lesson(groupId = "b", subjectRaw = "old friend")))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        store.friends += Friend("ДРУЖЕСКАЯ-ГРУППА", "#FF4CC38A", true, "Иван")
        store.failOnInsertGroupId = "b"
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") }, universityLoader = { directSchedule(it) })
        val before = store.dump()

        assertFalse(api.refresh())
        assertEquals(before, store.dump())
    }

    @Test
    fun profile_stop_during_direct_download_does_not_commit() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "old")))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        lateinit var api: ApiRefreshCoordinator
        api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") }, universityLoader = { names ->
                api.stop()
                directSchedule(names)
            })
        val before = store.dump()

        assertFalse(api.refresh())
        assertEquals(before, store.dump())
    }

    @Test
    fun older_healthy_api_snapshot_does_not_replace_newer_direct_download() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        TimetableApiCache(store).applyUniversity(directSchedule(listOf("ТЕСТ-ГРУППА")),
            listOf("ТЕСТ-ГРУППА"), store.settings(), emptyList(), "2026-10-02T12:00+03:00") {}
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { call -> jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else scheduleJson()) },
            universityLoader = { directSchedule(it) },
            clock = { OffsetDateTime.parse("2026-10-03T12:00:00+03:00") })

        assertTrue(api.refresh())
        assertEquals("fresh", store.allLessons("a").single().subjectRaw)
        assertEquals("university", store.readMetadata("a")?.source)
        assertEquals("2026-10-03T12:00+03:00", store.readMetadata("a")?.fetchedAt)
    }

    @Test
    fun cancelled_refresh_during_direct_download_does_not_commit() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("a", "ТЕСТ-ГРУППА"))
        store.replaceLessons("a", listOf(Lesson(groupId = "a", subjectRaw = "old")))
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/",
            FakeHttp { throw java.io.IOException("down") }, universityLoader = { names ->
                currentCoroutineContext().cancel()
                directSchedule(names)
            })
        val before = store.dump()

        val task = async { api.refresh() }
        try { task.await() } catch (_: kotlinx.coroutines.CancellationException) {}
        assertEquals(before, store.dump())
    }

    private fun directSchedule(names: List<String>): ParsedSchedule = ParsedSchedule(
        groups = names.map { GroupInfo(it, it) },
        lessons = names.map { Lesson(groupId = it, subjectRaw = "fresh") },
        periodStart = LocalDate.of(2026, 9, 1),
        weekCount = 2,
        periodTitle = "Тестовый семестр"
    )

    @Test
    fun friend_groups_are_fetched_with_selected() = runBlocking {
        val http = FakeHttp { call ->
            jsonReply((
                if (call.url.substringBefore('?').endsWith("/groups")) catalogJson(PIN, "a", "b")
                else if (call.url.contains("/b/")) scheduleJson("b")
                else scheduleJson("a")
            ).replace("\"id\":\"a\",\"name\":\"ТЕСТ-ГРУППА\"", "\"id\":\"a\",\"name\":\"МОЯ-ГРУППА\""))
        }
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(myGroupId = "a"))
        store.friends += Friend("ТЕСТ-ГРУППА", "#FF4CC38A", true, "Иван")
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        assertTrue(api.refresh())
        assertNotNull(store.readMetadata("a"))
        assertNotNull(store.readMetadata("b"))
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
    @Test fun channel_group_download_does_not_change_study_group_or_settings() = runBlocking {
        val http = FakeHttp { call -> jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson(PIN, "a") else scheduleJson()) }
        val store = MemoryTimetableStore()
        store.saveSettings(store.settings().copy(myGroupId = "other"))
        val before = store.settings()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http)
        assertTrue(api.ensureGroup("ТЕСТ-ГРУППА"))
        assertEquals(before, store.settings())
        assertNotNull(store.readMetadata("a"))
        val requests = http.requests.size
        assertTrue(api.ensureGroup("ТЕСТ-ГРУППА"))
        assertEquals(requests, http.requests.size)
    }

}
