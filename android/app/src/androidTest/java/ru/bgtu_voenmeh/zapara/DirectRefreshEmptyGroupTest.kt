package ru.bgtu_voenmeh.zapara

import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.VoenmehScheduleClient
import ru.bgtu_voenmeh.zapara.data.api.StaleTimetableSelection
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import java.util.concurrent.atomic.AtomicBoolean

@RunWith(AndroidJUnit4::class)
class DirectRefreshEmptyGroupTest {
    @Test
    fun selection_change_during_direct_refresh_keeps_last_good_and_old_freshness() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val db = Room.inMemoryDatabaseBuilder(context, ZaparaDatabase::class.java).build()
        try {
            val repo = ScheduleRepository(db)
            db.groupDao().upsert(GroupEntity("A", "A"))
            db.groupDao().upsert(GroupEntity("B", "B"))
            db.lessonDao().insertAll(listOf(
                LessonEntity(groupId = "A", dayOfWeek = 1, parity = 1, idx = 1, subjectRaw = "Old A"),
                LessonEntity(groupId = "B", dayOfWeek = 1, parity = 1, idx = 1, subjectRaw = "Old B")
            ))
            val oldStamp = "2026-10-01T00:00:00Z"
            repo.saveSettings(repo.settings().copy(myGroupId = "A", useUniversityXml = true,
                lastFetchedAt = oldStamp))
            val servedA = AtomicBoolean(false)
            repo.voenmeh = VoenmehScheduleClient { url ->
                when {
                    url.endsWith("/meta") ->
                        """{"has_data":true,"period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["A","B"],"updated_at":"2026-10-04T09:00:00Z"}"""
                    url.contains("/lessons") && url.contains("name=A") -> {
                        servedA.set(true)
                        repo.saveSettings(repo.settings().copy(myGroupId = "B"))
                        """{"name":"A","lessons":[{"day":1,"time":"9:00","week":"odd","kind":"лек","subject":"New A","teachers":[],"rooms":[]}]}"""
                    }
                    else -> error("Unexpected direct schedule request: $url")
                }
            }

            val failure = runCatching { repo.refresh(url = "http://127.0.0.1:1/unused.xml") }.exceptionOrNull()

            assertTrue("The old selection's schedule response must have arrived", servedA.get())
            assertTrue("A superseded direct response must be rejected without XML fallback: $failure",
                failure is StaleTimetableSelection)
            assertEquals("B", repo.settings().myGroupId)
            assertEquals(oldStamp, repo.settings().lastFetchedAt)
            assertEquals(listOf("Old A"), repo.allForGroup("A").map { it.subjectRaw })
            assertEquals(listOf("Old B"), repo.allForGroup("B").map { it.subjectRaw })
        } finally {
            db.close()
        }
    }

    @Test
    fun empty_direct_group_clears_saved_lessons_without_erasing_unfetched_group() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val db = Room.inMemoryDatabaseBuilder(context, ZaparaDatabase::class.java).build()
        try {
            val repo = ScheduleRepository(db)
            db.groupDao().upsert(GroupEntity("A", "A"))
            db.groupDao().upsert(GroupEntity("B", "B"))
            db.lessonDao().insertAll(listOf(
                LessonEntity(groupId = "A", dayOfWeek = 1, parity = 1, idx = 1, subjectRaw = "Old A"),
                LessonEntity(groupId = "B", dayOfWeek = 1, parity = 1, idx = 1, subjectRaw = "Last good B")
            ))
            repo.saveSettings(repo.settings().copy(myGroupId = "A"))
            repo.voenmeh = VoenmehScheduleClient { url ->
                when {
                    url.endsWith("/meta") ->
                        """{"has_data":true,"period":"ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.","groups":["A","B"],"updated_at":"2026-10-04T09:00:00Z"}"""
                    url.contains("/lessons") && url.contains("name=A") ->
                        """{"name":"A","lessons":[]}"""
                    else -> error("Unexpected direct schedule request: $url")
                }
            }

            repo.refresh(url = "http://127.0.0.1:1/unused.xml")

            assertTrue(repo.allForGroup("A").isEmpty())
            assertEquals(listOf("Last good B"), repo.allForGroup("B").map { it.subjectRaw })
        } finally {
            db.close()
        }
    }
}
