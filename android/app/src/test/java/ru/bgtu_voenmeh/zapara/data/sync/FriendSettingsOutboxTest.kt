package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.api.MemoryTimetableStore
import ru.bgtu_voenmeh.zapara.data.api.ApiRefreshCoordinator
import ru.bgtu_voenmeh.zapara.data.api.TimetableStore
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.PIN
import ru.bgtu_voenmeh.zapara.data.api.catalogJson
import ru.bgtu_voenmeh.zapara.data.api.scheduleJson
import ru.bgtu_voenmeh.zapara.data.api.jsonReply
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import ru.bgtu_voenmeh.zapara.data.db.FriendDao
import ru.bgtu_voenmeh.zapara.data.db.FriendEntity
import ru.bgtu_voenmeh.zapara.data.db.GroupDao
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.SettingsDao
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import java.time.Clock
import java.time.ZoneOffset
import java.util.UUID

class FriendSettingsOutboxTest {
    @Test
    fun timetable_identity_adoption_enqueues_the_remapped_group_through_the_account_repository() = runBlocking {
        val h = FriendSettingsHarness()
        h.repo.saveSettings(h.repo.settings().copy(myGroupId = "42"))
        val backing = h.repo.store as MemoryTimetableStore
        backing.upsertGroup(GroupInfo("42", "О3313"))
        val store = object : TimetableStore by backing {
            override fun settings(): ScheduleRepository.SettingsState = h.repo.settings()
        }
        val http = FakeHttp { call ->
            jsonReply((if (call.url.substringBefore('?').endsWith("/groups")) catalogJson(PIN, "О3313") else scheduleJson("О3313"))
                .replace("ТЕСТ-ГРУППА", "О3313"))
        }
        val api = ApiRefreshCoordinator(store, ProfileWork(), "https://example.invalid/", http, h.repo::saveSettings)
        assertTrue(api.refresh())
        assertEquals("О3313", h.repo.settings().myGroupId)
        val value = h.outbox.payloadValue(h.outbox.pending().single()) as SettingsValue
        assertEquals("О3313", value.selectedGroupId)
        assertTrue(backing.allLessons("О3313").isNotEmpty())
    }
    @Test
    fun friend_editor_save_toggle_and_delete_enqueue_the_same_account_entity() {
        val h = FriendSettingsHarness()
        val actions = ru.bgtu_voenmeh.zapara.ui.friends.FriendEditorActions(h.repo)
        actions.save(null, "Е452Б", "Иван", "#4CC38A")
        val id = h.repo.friends().single().id
        val first = h.outbox.pending().single()
        val value = h.outbox.payloadValue(first) as FriendValue
        h.outbox.applyAck(first, SyncRecord("friend", first.entityId, 1, false, NOW, value))
        actions.toggle(id, false)
        assertFalse((h.outbox.payloadValue(h.outbox.pending().single()) as FriendValue).enabled)
        actions.save(id, "Е452Б", "Иван, Мария", "#4CC38A")
        assertEquals("Иван, Мария", (h.outbox.payloadValue(h.outbox.pending().single()) as FriendValue).memberNames)
        actions.delete(id)
        val deletion = h.outbox.pending().single()
        assertEquals("delete", deletion.action)
        assertEquals(first.entityId, deletion.entityId)
        assertEquals(1L, deletion.expectedRevision)
        assertTrue(h.repo.friends().isEmpty())
    }

    @Test
    fun guest_friend_and_settings_leave_outbox_empty() {
        val h = FriendSettingsHarness(enabled = false)
        h.repo.insertFriend(FriendEntity(groupName = "Е452Б", colorHex = "#4CC38A", enabled = true))
        h.repo.updateFriend(h.repo.friends().single().copy(memberNames = "Иван", enabled = false))
        h.repo.deleteFriend(h.repo.friends().single().id)
        h.repo.saveSettings(h.repo.settings().copy(parityInvert = true, myGroupId = "3313"))
        assertFalse(h.outbox.enabled)
        assertTrue(h.outbox.pending().isEmpty())
        assertTrue(h.repo.friends().isEmpty())
        assertTrue(h.repo.settings().parityInvert)
    }

    @Test
    fun account_friend_add_update_delete_write_outbox_in_the_same_commit() {
        val h = FriendSettingsHarness()
        h.groups.upsert(GroupEntity("e452b", "Е452Б"))
        val id = h.repo.insertFriend(
            FriendEntity(groupName = "Е452Б", colorHex = "#4CC38A", enabled = true, memberNames = "Иван")
        )
        val inserted = h.outbox.pending().single()
        assertEquals("friend", inserted.entityType)
        assertEquals("upsert", inserted.action)
        assertEquals(0L, inserted.expectedRevision)
        assertEquals("pending", inserted.status)
        assertEquals(id, inserted.localRowId)
        assertNotEquals(UUID(0, 0), inserted.entityId)
        val created = h.outbox.payloadValue(inserted) as FriendValue
        assertEquals("e452b", created.groupId)
        assertEquals("Е452Б", created.groupName)
        assertEquals("Иван", created.memberNames)
        assertEquals(2, created.paletteIndex)
        assertTrue(created.enabled)
        assertEquals("Иван", h.repo.friends().single().memberNames)

        h.repo.updateFriend(h.repo.friends().single().copy(enabled = false, memberNames = "Пётр"))
        val updated = h.outbox.pending().single()
        assertEquals("upsert", updated.action)
        assertEquals(inserted.entityId, updated.entityId)
        val afterUpdate = h.outbox.payloadValue(updated) as FriendValue
        assertEquals("Пётр", afterUpdate.memberNames)
        assertFalse(afterUpdate.enabled)
        assertFalse(h.repo.friends().single().enabled)

        h.outbox.applyAck(
            updated,
            SyncRecord("friend", updated.entityId, 1, false, NOW, afterUpdate)
        )
        assertTrue(h.outbox.pending().isEmpty())
        h.repo.deleteFriend(id)
        assertTrue(h.repo.friends().isEmpty())
        val deleted = h.outbox.pending().single()
        assertEquals("friend", deleted.entityType)
        assertEquals("delete", deleted.action)
        assertEquals(1L, deleted.expectedRevision)
        assertEquals(updated.entityId, deleted.entityId)
    }

    @Test
    fun settings_round_trip_keeps_last_good_fetch_stamp_when_api_overlay_hides_it() {
        val h = FriendSettingsHarness()
        h.repo.store.useApiCatalog = true
        h.repo.saveSettings(h.repo.settings().copy(myGroupId = "42", lastFetchedAt = "2026-09-01T00:00:00Z"))
        assertNull(h.repo.settings().lastFetchedAt)
        h.repo.saveSettings(h.repo.settings().copy(parityInvert = true))
        assertTrue(h.repo.settings().parityInvert)
        assertEquals("2026-09-01T00:00:00Z", h.settings.row!!.lastFetchedAt)
        h.repo.saveSettings(h.repo.settings().copy(useUniversityXml = true))
        assertEquals("2026-09-01T00:00:00Z", h.repo.settings().lastFetchedAt)
    }

    @Test
    fun account_settings_sync_fields_enqueue_non_sync_does_not() {
        val h = FriendSettingsHarness()
        h.repo.saveSettings(h.repo.settings().copy(theme = "light", animations = false))
        assertTrue(h.outbox.pending().isEmpty())
        assertEquals("light", h.repo.settings().theme)

        h.repo.saveSettings(
            h.repo.settings().copy(
                myGroupId = "3313",
                parityInvert = true,
                notifyTime1 = "08:00",
                notifyTime2 = "18:30",
                intersectionStrictness = 50,
                alwaysShowAllTrafficLights = true
            )
        )
        val row = h.outbox.pending().single()
        assertEquals("settings", row.entityType)
        assertEquals("upsert", row.action)
        assertEquals(SyncValidation.SETTINGS_ID, row.entityId)
        assertEquals(1L, row.localRowId)
        assertEquals(0L, row.expectedRevision)
        val value = h.outbox.payloadValue(row) as SettingsValue
        assertEquals("3313", value.selectedGroupId)
        assertTrue(value.parityInvert)
        assertEquals("08:00", value.notifyTime1)
        assertEquals("18:30", value.notifyTime2)
        assertEquals(50, value.strictness)
        assertTrue(value.alwaysShow)

        h.repo.saveSettings(h.repo.settings().copy(theme = "dark", language = "ru"))
        assertEquals(1, h.outbox.pending().size)
        assertEquals("dark", h.repo.settings().theme)
    }

    @Test
    fun crash_before_commit_drops_friend_and_outbox() {
        val h = FriendSettingsHarness()
        h.outbox.beforeCommit = { throw IllegalStateException("crash") }
        try {
            h.repo.insertFriend(FriendEntity(groupName = "Е452Б", colorHex = "#F2A33C"))
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("crash", e.message)
        }
        assertTrue(h.repo.friends().isEmpty())
        assertTrue(h.outbox.pending().isEmpty())
    }

    @Test
    fun crash_during_settings_keeps_previous_domain_and_outbox() {
        val h = FriendSettingsHarness()
        h.repo.saveSettings(h.repo.settings().copy(parityInvert = true))
        val before = h.outbox.pending().single()
        h.outbox.beforeCommit = { throw IllegalStateException("crash") }
        try {
            h.repo.saveSettings(h.repo.settings().copy(parityInvert = false, myGroupId = "3313"))
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("crash", e.message)
        }
        assertTrue(h.repo.settings().parityInvert)
        assertEquals(null, h.repo.settings().myGroupId)
        val row = h.outbox.pending().single()
        assertEquals(before.opId, row.opId)
        assertTrue((h.outbox.payloadValue(row) as SettingsValue).parityInvert)
    }

    @Test
    fun unsynced_friend_delete_cancels_pending_upsert() {
        val h = FriendSettingsHarness()
        val id = h.repo.insertFriend(FriendEntity(groupName = "Е452Б", colorHex = "#C77DFF"))
        assertEquals("upsert", h.outbox.pending().single().action)
        h.repo.deleteFriend(id)
        assertTrue(h.repo.friends().isEmpty())
        assertTrue(h.outbox.pending().none { it.entityType == "friend" })
    }
}

private val FRIEND_CLOCK: Clock = Clock.fixed(CREATED, ZoneOffset.UTC)

private class FriendSettingsHarness(enabled: Boolean = true) {
    val friends = SnapshotFriendDao()
    val settings = SnapshotSettingsDao()
    val groups = SnapshotGroupDao()
    val commands = MemorySyncOutboxCommands()
    val state = MemorySyncStateCommands()
    val outbox = RoomSyncOutbox(
        enabled = enabled,
        commands = commands,
        state = state,
        transactor = { action ->
            val fItems = friends.items.map { it.copy() }
            val fSeq = friends.seq
            val sRow = settings.row?.copy()
            val gItems = groups.items.map { it.copy() }
            val box = commands.pending().map { it.copy(payload = it.payload?.copyOf()) }
            val st = state.get()
            try {
                action()
            } catch (t: Throwable) {
                friends.items.clear()
                friends.items.addAll(fItems)
                friends.seq = fSeq
                settings.row = sRow
                groups.items.clear()
                groups.items.addAll(gItems)
                for (row in commands.pending().toList()) commands.delete(row.opId)
                for (row in box) commands.upsert(row)
                if (st != null) state.upsert(st)
                throw t
            }
        },
        clock = FRIEND_CLOCK
    )
    val repo = ScheduleRepository(
        MemoryTimetableStore(),
        friends,
        settings,
        groups,
        outbox
    )
}

private class SnapshotFriendDao : FriendDao {
    val items = mutableListOf<FriendEntity>()
    var seq = 1L
    override fun insert(friend: FriendEntity): Long {
        val id = if (friend.id != 0L) friend.id else seq++
        if (friend.id != 0L) seq = maxOf(seq, friend.id + 1)
        items.removeAll { it.id == id }
        items.add(friend.copy(id = id))
        return id
    }
    override fun update(friend: FriendEntity) {
        val i = items.indexOfFirst { it.id == friend.id }
        if (i >= 0) items[i] = friend
    }
    override fun getAll(): List<FriendEntity> = items.toList()
    override fun delete(id: Long) {
        items.removeAll { it.id == id }
    }
}

private class SnapshotSettingsDao : SettingsDao {
    var row: SettingsEntity? = null
    override fun observe(): Flow<SettingsEntity?> = flowOf(row)
    override fun get(): SettingsEntity? = row
    override fun save(settings: SettingsEntity) {
        row = settings.copy(id = 1)
    }
}

private class SnapshotGroupDao : GroupDao {
    val items = mutableListOf<GroupEntity>()
    override fun upsert(group: GroupEntity) {
        items.removeAll { it.id == group.id }
        items.add(group)
    }
    override fun getAll(): List<GroupEntity> = items.sortedBy { it.name }
    override fun getById(id: String): GroupEntity? = items.firstOrNull { it.id == id }
}
