package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.util.UUID

class PrivateSyncCoordinatorTest {
    @Test
    fun deleting_a_new_row_during_its_upload_still_sends_server_delete_after_ack() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        h.outbox.setEpoch(EPOCH, 0)
        val deleteOp = UUID.randomUUID()
        val http = FakeHttp {
            h.outbox.enqueue(deleteOp, "homework", ENTITY, 0, "delete", null, 1)
            jsonReply(200, mutationResultJson(200, "applied", metadataJson(current = 1),
                String(SyncJson.serialize(homeworkRecord()))))
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pushPending()
        assertEquals(deleteOp, h.outbox.pending().single().opId)
        assertEquals("delete", h.outbox.pending().single().action)
        assertEquals(1L, h.outbox.pending().single().expectedRevision)
    }

    @Test
    fun edit_during_mutation_retains_new_operation_and_rebases_ack_revision() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue("sent"), 1)
        h.outbox.setEpoch(EPOCH, 0)
        val nextOp = UUID.randomUUID()
        val http = FakeHttp {
            h.outbox.enqueue(nextOp, "homework", ENTITY, 0, "upsert", homeworkValue("edited"), 1)
            jsonReply(200, mutationResultJson(200, "applied", metadataJson(current = 1),
                String(SyncJson.serialize(homeworkRecord(value = homeworkValue("sent"))))))
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pushPending()
        assertNull(h.outbox.find(OP))
        val pending = h.outbox.pending().single()
        assertEquals(nextOp, pending.opId)
        assertEquals(1L, pending.expectedRevision)
        assertEquals("edited", (h.outbox.payloadValue(pending) as HomeworkValue).text)
    }

    @Test
    fun revision_conflict_preserves_local_operation_and_stops_retries() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 1, "upsert", homeworkValue("local"), 1)
        h.outbox.setEpoch(EPOCH, 0)
        val http = FakeHttp {
            jsonReply(409, mutationResultJson(409, "revision_conflict", metadataJson(EPOCH, 2, 0),
                String(SyncJson.serialize(homeworkRecord(revision = 2, value = homeworkValue("server"))))))
        }
        h.coordinator.attach(client(http), { ACCESS }, background = false)
        h.coordinator.pushPending()
        assertEquals("conflict", h.outbox.find(OP)!!.status)
        assertEquals("local", (h.outbox.payloadValue(h.outbox.find(OP)!!) as HomeworkValue).text)
        h.coordinator.pushPending()
        assertEquals(1, http.requests.size)
    }

    @Test
    fun unattached_push_leaves_pending() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        h.coordinator.pushPending()
        assertEquals(1, h.outbox.pending().size)
        assertFalse(h.coordinator.attached)
    }

    @Test
    fun push_acks_pending_homework() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        h.outbox.setEpoch(EPOCH, 0)
        val http = FakeHttp { call ->
            assertTrue(call.url.endsWith("/api/v1/sync/mutations"))
            val record = homeworkRecord(entityId = ENTITY, revision = 1, value = homeworkValue())
            jsonReply(200, mutationResultJson(200, "applied", metadataJson(EPOCH, 1, 0), String(SyncJson.serialize(record))))
        }
        h.coordinator.attach(client(http), { ACCESS }, background = false)
        h.coordinator.pushPending()
        assertTrue(h.outbox.pending().isEmpty())
        assertEquals(1, http.requests.size)
        assertTrue(h.coordinator.attached)
    }

    @Test
    fun mutate_410_clears_stamped_epochs_and_keeps_rows() = runBlocking {
        val h = Harness()
        val completionOp = UUID.fromString("55555555-5555-5555-5555-555555555555")
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        h.outbox.enqueue(completionOp, "completion", ENTITY, 0, "upsert", completionValue(), null)
        val oldEpoch = UUID.fromString("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
        h.outbox.setEpoch(oldEpoch, 0)
        h.outbox.buildMutation(h.outbox.pending().first(), oldEpoch)
        assertEquals(oldEpoch, h.outbox.find(OP)!!.syncEpoch)
        val http = FakeHttp {
            jsonReply(410, mutationResultJson(410, "sync_reset", metadataJson(UUID.fromString("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), 0, 0)))
        }
        h.coordinator.attach(client(http), { ACCESS }, background = false)
        h.coordinator.pushPending()
        assertEquals(2, h.outbox.pending().size)
        assertNull(h.outbox.find(OP)!!.syncEpoch)
        assertNull(h.outbox.find(completionOp)!!.syncEpoch)
        assertEquals(1, http.requests.size)
    }

    @Test
    fun stale_profile_work_does_not_ack() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        h.outbox.setEpoch(EPOCH, 0)
        val http = FakeHttp {
            jsonReply(200, mutationResultJson(200, "applied", metadataJson(EPOCH, 1, 0), String(SyncJson.serialize(homeworkRecord(value = homeworkValue())))))
        }
        h.coordinator.attach(client(http), { ACCESS }, background = false)
        h.work.stopAccepting()
        h.coordinator.pushPending()
        assertEquals(1, h.outbox.pending().size)
        assertTrue(http.requests.isEmpty())
    }

    private class Harness {
        val work = ProfileWork()
        val outbox = RoomSyncOutbox(
            enabled = true,
            commands = MemorySyncOutboxCommands(),
            state = MemorySyncStateCommands()
        )
        val coordinator = PrivateSyncCoordinator(outbox, work)
    }
}
