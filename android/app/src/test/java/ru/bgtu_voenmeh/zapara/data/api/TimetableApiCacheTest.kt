package ru.bgtu_voenmeh.zapara.data.api

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Lesson
import java.time.LocalDate
import java.util.UUID

class TimetableApiCacheTest {
    private fun snapshot(vararg downloaded: String): TimetableApiSnapshot {
        val meta = TimetableApiMeta(
            snapshotId = UUID.randomUUID().toString(),
            fetchedAt = "2026-09-08T10:00:00Z",
            publishedAt = "2026-09-08T10:01:00Z",
            sourceModifiedAt = null,
            sourceKind = "file",
            sourceUrl = null,
            sourceSha256 = SHA,
            stale = false
        )
        val refresh = TimetableApiRefresh(null, null, null, null, null, false)
        val groups = listOf(
            TimetableApiGroup("1", "A", 1),
            TimetableApiGroup("2", "B", 1),
            TimetableApiGroup("9999", "Empty", 0)
        )
        val lesson = TimetableApiLesson(1, 1, 1, "09:00", "10:35", "Math", "math", null, null, null, null, null)
        val period = TimetableApiPeriod(LocalDate.of(2026, 9, 1), 2, "Autumn", "Europe/Moscow")
        val map = groups.filter { it.id in downloaded }.associate { g ->
            g.id to TimetableApiDownloadedGroup(
                g, meta, refresh,
                if (g.lessonCount == 0) emptyList() else listOf(lesson)
            )
        }
        return TimetableApiSnapshot(period, meta, refresh, groups, map)
    }

    @Test
    fun catalog_only_is_not_downloaded_empty_and_does_not_replace_existing_cache() {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("1", "Old", "original"))
        store.replaceLessons("1", listOf(Lesson(groupId = "1", subjectRaw = "old")))
        TimetableApiCache(store).apply(snapshot())
        assertNull(store.readMetadata("9999"))
        assertEquals("original", store.groups().first { it.id == "1" }.url)
        assertEquals("old", store.allLessons("1").single().subjectRaw)
        assertNull(store.settings().lastFetchedAt)
        TimetableApiCache(store).apply(snapshot("9999"))
        assertNotNull(store.readMetadata("9999"))
        assertTrue(store.allLessons("9999").isEmpty())
    }

    @Test
    fun trigger_failure_rolls_back_multiple_groups_catalog_and_metadata() {
        val store = MemoryTimetableStore()
        val cache = TimetableApiCache(store)
        cache.apply(snapshot("1", "2"))
        val before = store.dump()
        store.failOnInsertGroupId = "2"
        try {
            cache.apply(snapshot("1", "2"))
            fail()
        } catch (_: IllegalStateException) {
        }
        assertEquals(before, store.dump())
    }

    @Test
    fun constructed_zero_snapshot_is_rejected_before_writing() {
        val store = MemoryTimetableStore()
        val snap = snapshot()
        try {
            TimetableApiCache(store).apply(snap.copy(meta = snap.meta.copy(snapshotId = "00000000-0000-0000-0000-000000000000")))
            fail()
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.InvalidPayload, e.failure)
        }
        assertTrue(store.groups().isEmpty())
    }

    @Test
    fun unfetched_groups_keep_own_period_and_missing_catalog_does_not_delete_cache() {
        val store = MemoryTimetableStore().also { it.useApiCatalog = true }
        val cache = TimetableApiCache(store)
        val first = snapshot("1", "2")
        cache.apply(first)
        val newerBase = snapshot("1")
        val newer = newerBase.copy(
            period = newerBase.period.copy(start = LocalDate.of(2027, 2, 1), title = "Spring"),
            groups = newerBase.groups.filter { it.id != "2" }
        )
        cache.apply(newer)
        assertTrue("2" !in store.catalogIds())
        assertEquals(1, store.allLessons("2").size)
        assertEquals(first.meta.snapshotId, store.readMetadata("2")!!.meta!!.snapshotId)
        store.saveSettings(store.settings().copy(myGroupId = "2"))
        assertEquals(LocalDate.of(2026, 9, 1), store.settings().periodStart)
        assertTrue(!cache.canIntersect("1", "2"))
        store.saveSettings(store.settings().copy(myGroupId = "1"))
        assertEquals(LocalDate.of(2027, 2, 1), store.settings().periodStart)
    }

    @Test
    fun personal_homework_survives_catalog_disappearance() {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("gone", "Gone"))
        store.replaceLessons("gone", listOf(Lesson(groupId = "gone", subjectRaw = "Keep")))
        store.homeworkText = "personal"
        TimetableApiCache(store).apply(snapshot("1"))
        assertEquals("personal", store.homeworkText)
        assertEquals("Keep", store.allLessons("gone").single().subjectRaw)
    }
}
