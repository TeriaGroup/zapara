package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.HomeworkService
import ru.bgtu_voenmeh.zapara.data.db.*
import java.time.Instant
import java.time.LocalDate
import java.util.UUID

class RoomSyncProjectionTest {
    @Test fun restoring_tombstoned_completion_keeps_new_completion_linked_to_restored_homework() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord(), completionRecord(revision = 2)))
        val id = h.homework.getAll().single().id
        h.outbox.enqueue(OP, "completion", ENTITY, 2, "upsert", completionValue(), id)
        h.change(SyncRecord("completion", ENTITY, 3, true, NOW, null))
        assertTrue(h.outbox.inbox.resolve(h.outbox.inbox.conflicts().single(), keepLocal = true))
        val rows = h.outbox.pending()
        assertEquals(setOf("homework", "completion"), rows.map { it.entityType }.toSet())
        assertEquals(1, rows.map { it.entityId }.toSet().size)
        assertNotEquals(ENTITY, rows.first().entityId)
    }

    @Test fun new_epoch_replaces_legacy_creation_date_with_exact_received_metadata() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord(value = homeworkValue(legacy = LocalDate.of(2026, 9, 1)))))
        val snapshot = SyncResyncManifest(UUID.randomUUID(), UUID.randomUUID(), 1, NOW, NOW.plusSeconds(600), 1)
        h.outbox.inbox.begin(snapshot)
        h.outbox.inbox.stage(SyncResyncPage(snapshot, 0, 1, false, listOf(SyncManifestItem(1, homeworkRecord()))))
        h.outbox.inbox.publish()
        assertNull(h.outbox.identity("homework", h.homework.getAll().single().id)!!.legacyCreatedLocalDate)
    }

    @Test fun explicit_server_choice_applies_remote_value_and_removes_local_conflict() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord()))
        val id = h.homework.getAll().single().id
        h.outbox.enqueue(OP, "homework", ENTITY, 1, "upsert", homeworkValue("local"), id)
        h.change(homeworkRecord(revision = 2, value = homeworkValue("server")))
        val conflict = h.outbox.inbox.conflicts().single()
        assertTrue(h.outbox.inbox.resolve(conflict, keepLocal = false))
        assertEquals("server", h.homework.getById(id)!!.text)
        assertTrue(h.outbox.pending().isEmpty())
    }

    @Test fun explicit_local_choice_rebases_new_operation_while_preserving_server_creation_metadata() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord()))
        val id = h.homework.getAll().single().id
        h.outbox.enqueue(OP, "homework", ENTITY, 1, "upsert", homeworkValue("local", created = NOW), id)
        h.change(homeworkRecord(revision = 2, value = homeworkValue("server")))
        assertTrue(h.outbox.inbox.resolve(h.outbox.inbox.conflicts().single(), keepLocal = true))
        val pending = h.outbox.pending().single()
        assertNotEquals(OP, pending.opId)
        assertEquals(2L, pending.expectedRevision)
        assertEquals(ENTITY, pending.entityId)
        assertEquals("local", (h.outbox.payloadValue(pending) as HomeworkValue).text)
        assertEquals(CREATED, (h.outbox.payloadValue(pending) as HomeworkValue).createdAtUtc)
        assertEquals("local", h.homework.getById(id)!!.text)
    }

    @Test fun local_choice_after_remote_delete_recreates_homework_and_completion_with_new_shared_id() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord(), completionRecord(revision = 2)))
        val id = h.homework.getAll().single().id
        h.outbox.enqueue(OP, "homework", ENTITY, 1, "upsert", homeworkValue("local"), id)
        h.change(SyncRecord("homework", ENTITY, 3, true, NOW, null))
        assertTrue(h.outbox.inbox.resolve(h.outbox.inbox.conflicts().single(), keepLocal = true))
        val pending = h.outbox.pending()
        assertEquals(setOf("homework", "completion"), pending.map { it.entityType }.toSet())
        assertEquals(1, pending.map { it.entityId }.toSet().size)
        assertNotEquals(ENTITY, pending.first().entityId)
        assertTrue(pending.all { it.expectedRevision == 0L && it.status == "pending" })
        assertEquals("local", h.homework.getById(id)!!.text)
        assertEquals(pending.first().entityId, h.outbox.identity("completion", id)!!.entityId)
    }

    @Test fun conflict_choice_rejects_new_server_revision_until_user_sees_it() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord()))
        h.outbox.enqueue(OP, "homework", ENTITY, 1, "upsert", homeworkValue("local"), h.homework.getAll().single().id)
        h.change(homeworkRecord(revision = 2, value = homeworkValue("server 1")))
        val shown = h.outbox.inbox.conflicts().single()
        h.change(homeworkRecord(revision = 3, value = homeworkValue("server 2")))
        assertFalse(h.outbox.inbox.resolve(shown, keepLocal = false))
        assertEquals("conflict", h.outbox.find(OP)!!.status)
        assertEquals("server 2", (h.outbox.inbox.conflicts().single().serverRecord!!.value as HomeworkValue).text)
    }

    @Test fun snapshot_projects_all_five_types_and_preserves_local_preferences_and_creation_metadata() {
        val h = ProjectionHarness()
        h.settings.save(SettingsEntity(theme = "dark", animations = false, mapsAlpha = true,
            useUniversityXml = true, periodTitle = "semester", notifyEnabled = false))
        val creation = Instant.parse("2026-09-05T22:30:00Z")
        h.snapshot(listOf(
            completionRecord(revision = 3),
            homeworkRecord(revision = 2, value = homeworkValue(created = creation)),
            SyncRecord("override", OVERRIDE, 4, false, NOW,
                OverrideValue("лек ФИЗИКА", "лек физика", "weekday:2", "Physics", "note", CREATED)),
            SyncRecord("friend", FRIEND, 5, false, NOW, FriendValue("g", "Group", "Friend", 3, false)),
            SyncRecord("settings", SyncValidation.SETTINGS_ID, 6, false, NOW,
                SettingsValue("g", true, "21:00", "08:00", 80, true))
        ))
        val homework = h.homework.getAll().single()
        assertEquals("глава 1", homework.text)
        assertEquals("2026-09-06", homework.createdAt)
        assertEquals("done", homework.status)
        assertEquals(CREATED, h.outbox.identity("override", h.overrides.getAll().single().id)!!.createdAtUtc)
        assertEquals(creation, h.outbox.identity("homework", homework.id)!!.createdAtUtc)
        assertNull(h.outbox.identity("homework", homework.id)!!.legacyCreatedLocalDate)
        assertEquals(3L, h.outbox.identity("completion", homework.id)!!.revision)
        assertEquals("weekday:2", h.overrides.getAll().single().scope)
        assertEquals("#5AA9FF", h.friends.getAll().single().colorHex)
        assertFalse(h.friends.getAll().single().enabled)
        val settings = h.settings.get()!!
        assertEquals("g", settings.myGroupId)
        assertEquals(80, settings.intersectionStrictness)
        assertEquals("dark", settings.theme)
        assertFalse(settings.animations)
        assertFalse(settings.notifyEnabled)
        assertTrue(settings.mapsAlpha)
        assertTrue(settings.useUniversityXml)
        assertEquals("semester", settings.periodTitle)
        assertTrue(h.outbox.pending().isEmpty())

        // Editing received data must retain the original UTC instant and date-only metadata.
        HomeworkService(h.homework, { _, _, _ -> emptyList() }, { null }, h.outbox)
            .updateHomework(homework.id, "edited", 2)
        val sent = h.outbox.payloadValue(h.outbox.pending().single()) as HomeworkValue
        assertEquals(creation, sent.createdAtUtc)
        assertNull(sent.legacyCreatedLocalDate)
    }

    @Test fun completion_received_before_homework_is_durable_and_applies_when_homework_arrives() {
        val h = ProjectionHarness()
        h.snapshot(emptyList())
        h.change(completionRecord(revision = 1))
        assertTrue(h.homework.getAll().isEmpty())
        h.change(homeworkRecord(revision = 2))
        assertEquals("done", h.homework.getAll().single().status)
        assertEquals(1L, h.outbox.identity("completion", h.homework.getAll().single().id)!!.revision)
    }

    @Test fun tombstones_remove_projected_rows_and_reset_only_synced_settings() {
        val h = ProjectionHarness()
        h.settings.save(SettingsEntity(theme = "dark"))
        h.snapshot(listOf(homeworkRecord(), completionRecord(revision = 2),
            SyncRecord("override", OVERRIDE, 3, false, NOW,
                OverrideValue("ФИЗИКА", "физика", "global", "Physics", null, CREATED)),
            SyncRecord("friend", FRIEND, 4, false, NOW, FriendValue(null, "Group", "Friend", 1, true)),
            SyncRecord("settings", SyncValidation.SETTINGS_ID, 5, false, NOW, SettingsValue("g", true, null, null, 80, true))))
        h.change(SyncRecord("completion", ENTITY, 6, true, NOW, null))
        assertEquals("pending", h.homework.getAll().single().status)
        h.change(SyncRecord("homework", ENTITY, 7, true, NOW, null))
        h.change(SyncRecord("override", OVERRIDE, 8, true, NOW, null))
        h.change(SyncRecord("friend", FRIEND, 9, true, NOW, null))
        h.change(SyncRecord("settings", SyncValidation.SETTINGS_ID, 10, true, NOW, null))
        assertTrue(h.homework.getAll().isEmpty())
        assertTrue(h.overrides.getAll().isEmpty())
        assertTrue(h.friends.getAll().isEmpty())
        assertNull(h.settings.get()!!.myGroupId)
        assertEquals("dark", h.settings.get()!!.theme)
        assertTrue(h.outbox.pending().isEmpty())
    }

    @Test fun remote_homework_deletion_preserves_local_completion_and_its_conflict() {
        val h = ProjectionHarness()
        h.snapshot(listOf(homeworkRecord()))
        val id = h.homework.getAll().single().id
        h.homework.update(h.homework.getById(id)!!.copy(status = "done"))
        h.outbox.enqueue(OP, "completion", ENTITY, 0, "upsert", completionValue(), id)
        h.change(SyncRecord("homework", ENTITY, 2, true, NOW, null))
        assertEquals("done", h.homework.getById(id)!!.status)
        assertEquals("conflict", h.outbox.find(OP)!!.status)
        assertTrue(h.outbox.inbox.serverRecord("homework", ENTITY)!!.tombstone)
    }

    private companion object {
        val FRIEND: UUID = UUID.fromString("55555555-5555-5555-5555-555555555555")
        val OVERRIDE: UUID = UUID.fromString("66666666-6666-6666-6666-666666666666")
    }
}

private class ProjectionHarness {
    val homework = ReceiveHomeworkDao()
    val overrides = ReceiveOverrideDao()
    val friends = ReceiveFriendDao()
    val settings = ReceiveSettingsDao()
    val outbox = RoomSyncOutbox(true, MemorySyncOutboxCommands(), projection = RoomSyncProjection(homework, overrides, friends, settings))
    fun snapshot(records: List<SyncRecord>) {
        val manifest = SyncResyncManifest(MANIFEST_ID, EPOCH, records.maxOfOrNull { it.revision } ?: 0,
            NOW, NOW.plusSeconds(600), records.size.toLong())
        outbox.inbox.begin(manifest)
        if (records.isNotEmpty()) outbox.inbox.stage(SyncResyncPage(manifest, 0, records.size.toLong(), false,
            records.mapIndexed { index, record -> SyncManifestItem(index + 1L, record) }))
        outbox.inbox.publish()
    }
    fun change(record: SyncRecord) = outbox.inbox.applyChanges(SyncChangesPage(
        SyncMetadata(EPOCH, record.revision, 0), outbox.afterSequence, record.revision, false,
        listOf(SyncChange(record.revision, UUID.randomUUID(), record))))
}

private class ReceiveHomeworkDao : HomeworkDao {
    private val rows = linkedMapOf<Long, HomeworkEntity>()
    override fun getAll() = rows.values.toList()
    override fun getById(id: Long) = rows[id]
    override fun insert(e: HomeworkEntity): Long {
        val id = if (e.id == 0L) (rows.keys.maxOrNull() ?: 0) + 1 else e.id
        check(!rows.containsKey(id)); rows[id] = e.copy(id = id); return id
    }
    override fun update(e: HomeworkEntity) { if (rows.containsKey(e.id)) rows[e.id] = e }
    override fun deleteById(id: Long) = if (rows.remove(id) == null) 0 else 1
}
private class ReceiveOverrideDao : OverrideDao {
    private val rows = linkedMapOf<Long, OverrideEntity>()
    override fun getAll() = rows.values.toList()
    override fun insert(e: OverrideEntity): Long {
        val id = if (e.id == 0L) (rows.keys.maxOrNull() ?: 0) + 1 else e.id
        check(!rows.containsKey(id)); rows[id] = e.copy(id = id); return id
    }
    override fun deleteByKey(norm: String, scope: String): Int {
        val ids = rows.values.filter { it.subjectRawNormalized == norm && it.scope == scope }.map { it.id }
        ids.forEach { rows.remove(it) }; return ids.size
    }
    override fun deleteById(id: Long) = if (rows.remove(id) == null) 0 else 1
}
private class ReceiveFriendDao : FriendDao {
    private val rows = linkedMapOf<Long, FriendEntity>()
    override fun getAll() = rows.values.toList()
    override fun insert(friend: FriendEntity): Long {
        val id = if (friend.id == 0L) (rows.keys.maxOrNull() ?: 0) + 1 else friend.id
        check(!rows.containsKey(id)); rows[id] = friend.copy(id = id); return id
    }
    override fun update(friend: FriendEntity) { if (rows.containsKey(friend.id)) rows[friend.id] = friend }
    override fun delete(id: Long) { rows.remove(id) }
}
private class ReceiveSettingsDao : SettingsDao {
    private var row: SettingsEntity? = null
    override fun get() = row
    override fun save(settings: SettingsEntity) { row = settings }
    override fun observe(): Flow<SettingsEntity?> = flowOf(row)
}
