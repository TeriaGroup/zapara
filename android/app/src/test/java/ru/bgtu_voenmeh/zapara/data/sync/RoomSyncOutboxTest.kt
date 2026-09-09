package ru.bgtu_voenmeh.zapara.data.sync

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.HomeworkService
import ru.bgtu_voenmeh.zapara.data.OverrideService
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.db.HomeworkDao
import ru.bgtu_voenmeh.zapara.data.db.HomeworkEntity
import ru.bgtu_voenmeh.zapara.data.db.OverrideDao
import ru.bgtu_voenmeh.zapara.data.db.OverrideEntity
import ru.bgtu_voenmeh.zapara.data.db.SyncOutboxEntity
import ru.bgtu_voenmeh.zapara.data.db.SyncStateEntity
import java.time.Clock
import java.time.LocalDate
import java.time.ZoneOffset
import java.util.UUID

class RoomSyncOutboxTest {
    @Test
    fun guest_homework_mutations_leave_outbox_empty() {
        val h = Harness(enabled = false)
        h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE)
        h.homework.updateHomework(h.homework.all().single().id, "глава 2", 1)
        h.homework.markDone(h.homework.all().single().id, true)
        h.overrides.addOrUpdate("лек ФИЗИКА", "global", "Физика", "заметка")
        assertFalse(h.outbox.enabled)
        assertTrue(h.outbox.pending().isEmpty())
        assertEquals(1, h.homework.all().size)
        assertEquals(1, h.overrides.all().size)
    }

    @Test
    fun account_add_commits_homework_and_outbox_together() {
        val h = Harness()
        val id = h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE, OP)
        val row = h.outbox.pending().single()
        assertEquals(OP, row.opId)
        assertEquals("homework", row.entityType)
        assertEquals("upsert", row.action)
        assertEquals(0L, row.expectedRevision)
        assertEquals("pending", row.status)
        assertEquals(id, row.localRowId)
        assertNotEquals(UUID(0, 0), row.entityId)
        assertEquals("глава 1", (h.outbox.payloadValue(row) as HomeworkValue).text)
        assertEquals("глава 1", h.homework.getById(id)!!.text)
    }

    @Test
    fun exact_opId_retry_does_not_double_apply() {
        val h = Harness()
        val first = h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE, OP)
        val again = h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE, OP)
        assertEquals(first, again)
        assertEquals(1, h.homework.all().size)
        assertEquals(1, h.outbox.pending().size)
        try {
            h.homework.addHomework("лек ИСТОРИЯ", "другой текст", 1, CREATED_DATE, OP)
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("Повтор операции с другим содержимым.", e.message)
        }
        assertEquals("глава 1", h.homework.all().single().text)
        assertEquals(1, h.outbox.pending().size)
    }

    @Test
    fun account_update_and_delete_write_outbox_in_the_same_commit() {
        val h = Harness()
        val id = h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE)
        h.homework.updateHomework(id, "глава 2", 2)
        assertEquals("глава 2", h.homework.getById(id)!!.text)
        assertEquals(2, h.homework.getById(id)!!.n)
        val pending = h.outbox.pending().single()
        assertEquals("upsert", pending.action)
        assertEquals("pending", pending.status)
        h.outbox.applyAck(pending, homeworkRecord(entityId = pending.entityId, revision = 1))
        assertTrue(h.outbox.pending().isEmpty())
        h.homework.delete(id)
        assertTrue(h.homework.all().isEmpty())
        val deleted = h.outbox.pending().single()
        assertEquals("delete", deleted.action)
        assertEquals(1L, deleted.expectedRevision)
        assertEquals(pending.entityId, deleted.entityId)
    }

    @Test
    fun crash_before_commit_drops_both_domain_and_outbox() {
        val h = Harness()
        h.outbox.beforeCommit = { throw IllegalStateException("crash") }
        try {
            h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE)
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("crash", e.message)
        }
        assertTrue(h.homework.all().isEmpty())
        assertTrue(h.outbox.pending().isEmpty())
    }

    @Test
    fun crash_during_update_keeps_previous_domain_and_outbox() {
        val h = Harness()
        val id = h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE, OP)
        h.outbox.beforeCommit = { throw IllegalStateException("crash") }
        try {
            h.homework.updateHomework(id, "глава 2", 1)
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("crash", e.message)
        }
        assertEquals("глава 1", h.homework.getById(id)!!.text)
        val row = h.outbox.pending().single()
        assertEquals(OP, row.opId)
        assertEquals("upsert", row.action)
        assertEquals("глава 1", (h.outbox.payloadValue(row) as HomeworkValue).text)
    }

    @Test
    fun homework_delete_cancels_pending_completion_in_the_same_savepoint() {
        val h = Harness()
        val id = h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE)
        h.homework.markDone(id, true)
        val before = h.outbox.pending()
        assertTrue(before.any { it.entityType == "homework" && it.action == "upsert" })
        assertTrue(before.any { it.entityType == "completion" && it.action == "upsert" })
        val entityId = before.first { it.entityType == "homework" }.entityId
        h.outbox.beforeCommit = { throw IllegalStateException("crash") }
        try {
            h.homework.delete(id)
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("crash", e.message)
        }
        assertNotNull(h.homework.getById(id))
        assertTrue(h.outbox.pending().any { it.entityType == "completion" && it.entityId == entityId && it.action == "upsert" })
        h.outbox.beforeCommit = null
        h.homework.delete(id)
        assertTrue(h.homework.all().isEmpty())
        assertTrue(h.outbox.pending().none { it.entityType == "completion" })
        assertTrue(h.outbox.pending().none { it.entityType == "homework" && it.action == "upsert" })
    }

    @Test
    fun account_override_enqueues_guest_does_not() {
        val guest = Harness(enabled = false)
        guest.overrides.addOrUpdate("лек ФИЗИКА", "global", "Физика", "заметка")
        assertTrue(guest.outbox.pending().isEmpty())
        val h = Harness()
        val id = h.overrides.addOrUpdate("лек ФИЗИКА", "global", "Физика", "заметка")
        val row = h.outbox.pending().single()
        assertEquals("override", row.entityType)
        assertEquals("upsert", row.action)
        assertEquals(id, row.localRowId)
        assertEquals(0L, row.expectedRevision)
        h.overrides.remove(id)
        assertTrue(h.overrides.all().isEmpty())
        assertTrue(h.outbox.pending().none { it.entityType == "override" && it.action == "upsert" })
    }

    @Test
    fun abort_expired_epoch_clears_stamped_pending() {
        val h = Harness()
        h.homework.addHomework("лек ИСТОРИЯ", "глава 1", 1, CREATED_DATE, OP)
        val oldEpoch = UUID.fromString("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
        h.outbox.buildMutation(h.outbox.pending().single(), oldEpoch)
        assertEquals(oldEpoch, h.outbox.find(OP)!!.syncEpoch)
        assertTrue(h.outbox.abortIfReset(PrivateSyncState.ResetRequired))
        assertNull(h.outbox.find(OP)!!.syncEpoch)
        assertEquals(1, h.outbox.pending().size)
        assertFalse(h.outbox.abortIfReset(PrivateSyncState.Success))
    }

    @Test
    fun room_commands_round_trip_existing_outbox_entity() {
        val dao = FakeSyncOutboxDao()
        val cmds = RoomSyncOutboxCommands(dao)
        val row = SyncOutboxEntity(
            opId = OP.toString(),
            entityType = "homework",
            entityId = ENTITY.toString(),
            expectedRevision = 0,
            action = "upsert",
            payload = byteArrayOf(1),
            localRowId = 7,
            status = "pending",
            createdAtUtc = "2026-09-05T12:00:00Z",
            syncEpoch = EPOCH.toString()
        )
        cmds.upsert(row)
        assertEquals("homework", cmds.find(OP.toString())!!.entityType)
        cmds.clearPendingEpochs()
        assertNull(cmds.find(OP.toString())!!.syncEpoch)
        cmds.delete(OP.toString())
        assertNull(cmds.find(OP.toString()))
        val stateDao = FakeSyncStateDao()
        val state = RoomSyncStateCommands(stateDao)
        state.upsert(SyncStateEntity(1, EPOCH.toString(), 4))
        assertEquals(4L, state.get()!!.afterSequence)
    }
}

private val CLOCK: Clock = Clock.fixed(CREATED, ZoneOffset.UTC)
private val CREATED_DATE: LocalDate = LocalDate.of(2026, 9, 5)

private class Harness(enabled: Boolean = true) {
    val homeworkDao = SnapshotHomeworkDao()
    val overrideDao = SnapshotOverrideDao()
    val commands = MemorySyncOutboxCommands()
    val state = MemorySyncStateCommands()
    val outbox = RoomSyncOutbox(
        enabled = enabled,
        commands = commands,
        state = state,
        transactor = { action ->
            val hItems = homeworkDao.items.map { it.copy() }
            val hSeq = homeworkDao.seq
            val oItems = overrideDao.items.map { it.copy() }
            val oSeq = overrideDao.seq
            val box = commands.pending().map { it.copy(payload = it.payload?.copyOf()) }
            val st = state.get()
            try {
                action()
            } catch (t: Throwable) {
                homeworkDao.items.clear()
                homeworkDao.items.addAll(hItems)
                homeworkDao.seq = hSeq
                overrideDao.items.clear()
                overrideDao.items.addAll(oItems)
                overrideDao.seq = oSeq
                for (row in commands.pending().toList()) commands.delete(row.opId)
                for (row in box) commands.upsert(row)
                if (st != null) state.upsert(st)
                throw t
            }
        },
        clock = CLOCK
    )
    val homework = HomeworkService(
        homeworkDao,
        lessonsFor = { _, _, _ -> emptyList() },
        ctx = { SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false) },
        outbox
    )
    val overrides = OverrideService(overrideDao, outbox)
}

private class SnapshotHomeworkDao : HomeworkDao {
    val items = mutableListOf<HomeworkEntity>()
    var seq = 1L
    override fun getAll(): List<HomeworkEntity> = items.sortedBy { it.dueDateComputed }.toList()
    override fun getById(id: Long): HomeworkEntity? = items.firstOrNull { it.id == id }
    override fun insert(e: HomeworkEntity): Long {
        val id = if (e.id != 0L) e.id else seq++
        if (e.id != 0L) seq = maxOf(seq, e.id + 1)
        items.removeAll { it.id == id }
        items.add(e.copy(id = id))
        return id
    }
    override fun update(e: HomeworkEntity) {
        val i = items.indexOfFirst { it.id == e.id }
        if (i >= 0) items[i] = e
    }
    override fun deleteById(id: Long): Int {
        val n = items.count { it.id == id }
        items.removeAll { it.id == id }
        return n
    }
}

private class SnapshotOverrideDao : OverrideDao {
    val items = mutableListOf<OverrideEntity>()
    var seq = 1L
    override fun getAll(): List<OverrideEntity> = items.toList()
    override fun insert(e: OverrideEntity): Long {
        val id = if (e.id != 0L) e.id else seq++
        if (e.id != 0L) seq = maxOf(seq, e.id + 1)
        items.removeAll { it.id == id }
        items.add(e.copy(id = id))
        return id
    }
    override fun deleteByKey(norm: String, scope: String): Int {
        val n = items.count { it.subjectRawNormalized == norm && it.scope == scope }
        items.removeAll { it.subjectRawNormalized == norm && it.scope == scope }
        return n
    }
    override fun deleteById(id: Long): Int {
        val n = items.count { it.id == id }
        items.removeAll { it.id == id }
        return n
    }
}

private class FakeSyncOutboxDao : ru.bgtu_voenmeh.zapara.data.db.SyncOutboxDao {
    private val rows = LinkedHashMap<String, SyncOutboxEntity>()
    override fun listAll(): List<SyncOutboxEntity> =
        rows.values.sortedWith(compareBy({ it.createdAtUtc }, { it.opId }))
    override fun find(opId: String): SyncOutboxEntity? = rows[opId]
    override fun upsert(row: SyncOutboxEntity) { rows[row.opId] = row }
    override fun delete(opId: String) { rows.remove(opId) }
    override fun clearPendingEpochs() {
        for (row in rows.values.toList()) {
            if (row.status == "pending") rows[row.opId] = row.copy(syncEpoch = null)
        }
    }
}

private class FakeSyncStateDao : ru.bgtu_voenmeh.zapara.data.db.SyncStateDao {
    private var row: SyncStateEntity? = SyncStateEntity(1, null, 0)
    override fun get(): SyncStateEntity? = row
    override fun upsert(row: SyncStateEntity) { this.row = row }
}
