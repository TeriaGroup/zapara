package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text
import ru.bgtu_voenmeh.zapara.data.db.SyncOutboxEntity
import ru.bgtu_voenmeh.zapara.data.db.SyncStateEntity
import java.util.UUID

class PrivateSyncOutboxTest {
    @Test
    fun exact_opId_retry_keeps_one_row_changed_payload_throws() {
        val box = MemoryPrivateSyncOutbox()
        val first = homeworkValue("глава 1")
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", first, 1)
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", first, 1)
        assertEquals(1, box.pending().size)
        try {
            box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue("другой текст"), 1)
            fail()
        } catch (e: IllegalStateException) {
            assertEquals("Повтор операции с другим содержимым.", e.message)
        }
        assertEquals("глава 1", (box.buildMutation(box.pending().single(), EPOCH).value as HomeworkValue).text)
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue("глава 1", created = NOW), 1)
        assertEquals(1, box.pending().size)
    }

    @Test
    fun disabled_enqueue_is_ignored() {
        val box = MemoryPrivateSyncOutbox(enabled = false)
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        assertTrue(box.pending().isEmpty())
    }

    @Test
    fun delete_of_unacked_create_drops_both_and_later_upsert_replaces_pending() {
        val box = MemoryPrivateSyncOutbox()
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue("глава 1"), 1)
        box.enqueue(UUID.fromString("55555555-5555-5555-5555-555555555555"), "homework", ENTITY, 0, "delete", null, 1)
        assertTrue(box.pending().isEmpty())
        val first = UUID.fromString("66666666-6666-6666-6666-666666666666")
        val second = UUID.fromString("77777777-7777-7777-7777-777777777777")
        box.enqueue(first, "homework", ENTITY, 4, "upsert", homeworkValue("A"), 1)
        box.enqueue(second, "homework", ENTITY, 9, "upsert", homeworkValue("B"), 1)
        val row = box.pending().single()
        assertEquals(second, row.opId)
        assertEquals(4L, row.expectedRevision)
        assertEquals("B", (box.buildMutation(row, EPOCH).value as HomeworkValue).text)
    }

    @Test
    fun ack_drops_outbox_and_draft() {
        val box = MemoryPrivateSyncOutbox()
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        val row = box.pending().single()
        box.markConflict(row, null)
        assertEquals(1, box.drafts().size)
        box.applyAck(row, homeworkRecord(revision = 1))
        assertTrue(box.pending().isEmpty())
        assertTrue(box.drafts().isEmpty())
        assertFalse(box.hasPendingOrDraft("homework", ENTITY))
    }

    @Test
    fun revision_conflict_keeps_local_draft_and_does_not_last_write_wins() = runBlocking {
        val box = MemoryPrivateSyncOutbox()
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue("локальный черновик"), 1)
        val server = homeworkValue("серверная версия")
        val record = homeworkRecord(revision = 4, value = server)
        val http = FakeHttp {
            jsonReply(
                409,
                mutationResultJson(409, "revision_conflict", record = recordJson("homework", ENTITY, 4, false, homeworkValueJson(server)))
            )
        }
        val state = box.pushPending(client(http), ACCESS, EPOCH)
        assertEquals(PrivateSyncState.Conflict, state)
        val row = box.pending().single()
        assertEquals("conflict", row.status)
        val draft = box.drafts().single()
        assertEquals(ENTITY, draft.entityId)
        assertEquals(OP, draft.opId)
        assertTrue(draft.localPayload.contains("локальный черновик"))
        assertTrue(draft.serverPayload.contains("серверная версия"))
        assertEquals("локальный черновик", (box.payloadValue(row) as HomeworkValue).text)
        assertEquals(1, http.requests.size)
    }

    @Test
    fun op_id_reused_is_conflict_and_does_not_ack() = runBlocking {
        val box = MemoryPrivateSyncOutbox()
        box.enqueue(OP, "completion", ENTITY, 0, "upsert", completionValue(), null)
        val http = FakeHttp { jsonReply(409, mutationResultJson(409, "op_id_reused")) }
        assertEquals(PrivateSyncState.Conflict, box.pushPending(client(http), ACCESS, EPOCH))
        assertEquals("conflict", box.pending().single().status)
        assertEquals(OP, box.pending().single().opId)
    }

    @Test
    fun mutate_410_aborts_so_second_pending_is_not_restamped_with_expired_epoch() = runBlocking {
        val box = MemoryPrivateSyncOutbox()
        val homeworkOp = OP
        val completionOp = UUID.fromString("55555555-5555-5555-5555-555555555555")
        box.enqueue(homeworkOp, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        box.enqueue(completionOp, "completion", ENTITY, 0, "upsert", completionValue(), null)
        val oldEpoch = UUID.fromString("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
        val newEpoch = UUID.fromString("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")
        val epochs = mutableListOf<UUID>()
        val http = FakeHttp { call ->
            val sent = StrictJson.parse(call.body!!).obj()
            epochs += UUID.fromString(sent.text("syncEpoch", 36))
            jsonReply(
                410,
                mutationResultJson(410, "sync_reset", metadataJson(newEpoch, 0, 0))
            )
        }
        val first = box.pending().first()
        box.buildMutation(first, oldEpoch)
        assertEquals(oldEpoch, box.find(first.opId)!!.syncEpoch)
        assertEquals(PrivateSyncState.ResetRequired, box.pushPending(client(http), ACCESS, oldEpoch))
        assertEquals(listOf(oldEpoch), epochs)
        assertEquals(2, box.pending().size)
        assertTrue(box.pending().none { it.syncEpoch == oldEpoch })
        assertNull(box.find(completionOp)!!.syncEpoch)
        val next = box.buildMutation(box.find(completionOp)!!, newEpoch)
        assertEquals(newEpoch, next.syncEpoch)
        assertEquals(completionOp, next.opId)
    }

    @Test
    fun successful_mutate_acks_and_set_epoch_clears_stamped_rows_on_rotation() = runBlocking {
        val box = MemoryPrivateSyncOutbox()
        box.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        box.setEpoch(EPOCH, 0)
        box.buildMutation(box.pending().single(), EPOCH)
        assertEquals(EPOCH, box.find(OP)!!.syncEpoch)
        val http = FakeHttp { call ->
            val body = StrictJson.parse(call.body!!).obj()
            val entity = UUID.fromString(body.text("entityId", 36))
            jsonReply(
                200,
                mutationResultJson(200, "applied", metadataJson(EPOCH, 1, 0), recordJson("homework", entity, 1, false, homeworkValueJson(homeworkValue())))
            )
        }
        assertEquals(PrivateSyncState.Success, box.pushPending(client(http), ACCESS, EPOCH))
        assertTrue(box.pending().isEmpty())
        box.enqueue(UUID.fromString("88888888-8888-4888-8888-888888888888"), "homework", ENTITY, 1, "upsert", homeworkValue("глава 2"), 1)
        box.buildMutation(box.pending().single(), EPOCH)
        val rotated = UUID.fromString("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")
        box.setEpoch(rotated, 3)
        assertEquals(rotated, box.syncEpoch)
        assertEquals(3L, box.afterSequence)
        assertNull(box.pending().single().syncEpoch)
    }

    @Test
    fun room_ports_describe_existing_outbox_entity() {
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
            syncEpoch = null
        )
        val mem = MemorySyncOutboxCommands()
        mem.upsert(row)
        assertEquals("homework", mem.find(OP.toString())!!.entityType)
        mem.clearPendingEpochs()
        mem.delete(OP.toString())
        assertNull(mem.find(OP.toString()))
        val state = MemorySyncStateCommands()
        state.upsert(SyncStateEntity(1, EPOCH.toString(), 4))
        assertEquals(4L, state.get()!!.afterSequence)
    }
}

private fun homeworkValueJson(value: HomeworkValue): String {
    val legacy = value.legacyCreatedLocalDate?.let { "\"$it\"" } ?: "null"
    val created = value.createdAtUtc.toString().replace(".000Z", "Z")
    return """{"subjectRaw":${jsonStr(value.subjectRaw)},"subjectKey":${jsonStr(value.subjectKey)},"text":${jsonStr(value.text)},"targetNthOccurrence":${value.targetNthOccurrence},"createdAtUtc":"$created","legacyCreatedLocalDate":$legacy}"""
}

private fun jsonStr(value: String): String = buildString {
    append('"')
    for (c in value) when (c) {
        '\\' -> append("\\\\")
        '"' -> append("\\\"")
        else -> append(c)
    }
    append('"')
}
