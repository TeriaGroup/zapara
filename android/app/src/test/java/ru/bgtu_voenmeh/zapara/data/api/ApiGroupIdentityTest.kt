package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.net.URI

class ApiGroupIdentityTest {
    @Test
    fun selection_change_at_transaction_entry_is_not_overwritten_by_identity_adoption() = runBlocking {
        val memory = MemoryTimetableStore(); memory.upsertGroup(GroupInfo("42", "О3313"))
        memory.saveSettings(memory.settings().copy(myGroupId = "42"))
        val store = object : TimetableStore by memory {
            override fun runInTransaction(block: () -> Unit) {
                memory.saveSettings(memory.settings().copy(myGroupId = "new-user-choice"))
                memory.runInTransaction(block)
            }
        }
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { response(it, mapOf("О3313" to "О3313")) })
        assertFalse(api.refresh()); assertEquals("new-user-choice", memory.settings().myGroupId)
        assertNull(memory.readMetadata(""))
    }
    @Test
    fun needed_only_does_not_skip_a_friend_absent_from_the_previous_catalog() = runBlocking {
        val store = MemoryTimetableStore(); store.saveSettings(store.settings().copy(myGroupId = "О3313"))
        var names = mapOf("О3313" to "О3313"); var pin = PIN
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { response(it, names, pin) })
        assertTrue(api.refresh())
        store.friends += Friend("А4313", "#4CC38A", true, "Маша")
        names = mapOf("О3313" to "О3313", "А4313" to "А4313"); pin = NEW_PIN
        assertTrue(api.refresh(neededOnly = true)); assertEquals(NEW_PIN, store.readMetadata("А4313")?.meta?.snapshotId)
    }
    @Test
    fun switching_to_university_source_during_fetch_does_not_adopt_the_API_identity() = runBlocking {
        val store = MemoryTimetableStore(); store.upsertGroup(GroupInfo("42", "О3313"))
        store.saveSettings(store.settings().copy(myGroupId = "42"))
        val arrived = CompletableDeferred<Unit>(); val release = CompletableDeferred<Unit>()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { call ->
            arrived.complete(Unit); release.await(); response(call, mapOf("О3313" to "О3313"))
        })
        val refreshing = async { api.refresh() }; arrived.await()
        store.saveSettings(store.settings().copy(useUniversityXml = true))
        release.complete(Unit)
        assertFalse(refreshing.await()); assertEquals("42", store.settings().myGroupId)
        assertNull(store.readMetadata(""))
    }

    @Test
    fun failed_settings_commit_rolls_back_the_adopted_catalog_and_lessons() = runBlocking {
        val store = MemoryTimetableStore(); store.upsertGroup(GroupInfo("42", "О3313"))
        store.saveSettings(store.settings().copy(myGroupId = "42"))
        val before = store.dump(); var calls = 0
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { response(it, mapOf("О3313" to "О3313")) },
            saveSettings = { calls++; throw IllegalStateException("disk full") })
        assertFalse(api.refresh()); assertEquals(1, calls); assertEquals(before, store.dump())
    }
    @Test
    fun numeric_and_named_ids_follow_same_group_in_both_directions_without_wiping_friends_or_homework() = runBlocking {
        for ((oldId, newId, oldFriend, newFriend) in listOf(
            listOf("42", "О3313", "77", "А4313"), listOf("О3313", "42", "А4313", "77")
        )) {
            val store = MemoryTimetableStore()
            store.upsertGroup(GroupInfo(oldId, "О3313")); store.upsertGroup(GroupInfo(oldFriend, "А4313"))
            store.saveSettings(store.settings().copy(myGroupId = oldId))
            store.replaceLessons(oldId, listOf(Lesson(groupId = oldId, subjectRaw = "Старое расписание")))
            store.friends += Friend("А4313", "#4CC38A", true, "Маша")
            store.homeworkText = "Сохранить задание"
            val names = linkedMapOf(newId to "О3313", newFriend to "А4313")
            val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { response(it, names) })
            assertTrue(api.refresh())
            assertEquals(newId, store.settings().myGroupId)
            assertTrue(store.allLessons(newId).isNotEmpty()); assertTrue(store.allLessons(newFriend).isNotEmpty())
            assertEquals("Старое расписание", store.allLessons(oldId).single().subjectRaw)
            assertEquals("Маша", store.friends.single().memberNames)
            assertEquals("Сохранить задание", store.homeworkText)
        }
    }

    @Test
    fun failed_coherent_adoption_keeps_previous_selection_and_all_cached_data() = runBlocking {
        val store = MemoryTimetableStore()
        store.upsertGroup(GroupInfo("42", "О3313")); store.upsertGroup(GroupInfo("77", "А4313"))
        store.saveSettings(store.settings().copy(myGroupId = "42"))
        store.friends += Friend("А4313", "#4CC38A", true, "Маша")
        store.replaceLessons("42", listOf(Lesson(groupId = "42", subjectRaw = "Последнее хорошее")))
        store.failOnInsertGroupId = "А4313"
        val before = store.dump()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { response(it, linkedMapOf("О3313" to "О3313", "А4313" to "А4313")) })
        assertFalse(api.refresh()); assertEquals(before, store.dump())
        assertEquals("42", store.settings().myGroupId)
    }

    @Test
    fun ambiguous_name_does_not_guess_a_new_group() = runBlocking {
        val store = MemoryTimetableStore(); store.upsertGroup(GroupInfo("42", "О3313"))
        store.saveSettings(store.settings().copy(myGroupId = "42"))
        val before = store.dump()
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", FakeHttp { response(it, linkedMapOf("one" to "О3313", "two" to "О3313")) })
        assertFalse(api.refresh()); assertEquals(before, store.dump())
        assertEquals(TimetableApiFailure.UnknownRequiredGroup, api.lastFailure)
    }

    private fun response(call: HttpCall, names: Map<String, String>, pin: String = PIN): HttpReply {
        val path = URI(call.url).path
        return jsonReply(if (path.endsWith("/groups")) {
            val groups = names.entries.joinToString(",") { (id, name) -> """{"id":"$id","name":"$name","lessonCount":1}""" }
            envelope(pin).dropLast(1) + ""","groups":[$groups]}"""
        } else {
            val id = path.substringBeforeLast('/').substringAfterLast('/')
            scheduleJson(id, pin).replace("\"name\":\"ТЕСТ-ГРУППА\"", "\"name\":\"${names.getValue(id)}\"")
        })
    }
}
