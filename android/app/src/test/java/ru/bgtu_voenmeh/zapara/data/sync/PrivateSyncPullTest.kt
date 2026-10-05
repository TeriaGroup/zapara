package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.io.IOException
import java.util.UUID

class PrivateSyncPullTest {
    @Test fun change_feed_acknowledges_timed_out_upload_without_conflicting_with_later_local_edit() {
        val h = PullHarness()
        h.initialize()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue("sent"), 1)
        h.outbox.buildMutation(h.outbox.find(OP)!!, EPOCH)
        val edit = UUID.randomUUID()
        h.outbox.enqueue(edit, "homework", ENTITY, 0, "upsert", homeworkValue("new edit"), 1)
        h.inbox.applyChanges(SyncChangesPage(SyncMetadata(EPOCH, 1, 0), 0, 1, false,
            listOf(SyncChange(1, OP, homeworkRecord(value = homeworkValue("sent"))))))
        assertNull(h.outbox.find(OP))
        val pending = h.outbox.pending().single()
        assertEquals(edit, pending.opId)
        assertEquals("pending", pending.status)
        assertEquals(1L, pending.expectedRevision)
        assertEquals("new edit", (h.outbox.payloadValue(pending) as HomeworkValue).text)
        assertTrue(h.projected.isEmpty())
    }

    @Test fun retention_resync_keeps_pending_edit_when_its_base_revision_is_unchanged() {
        val h = PullHarness()
        h.initialize()
        h.outbox.enqueue(OP, "homework", ENTITY, 2, "upsert", homeworkValue("local"), 1)
        h.inbox.requireSnapshot()
        val snapshot = manifest()
        h.inbox.begin(snapshot)
        h.inbox.stage(SyncResyncPage(snapshot, 0, 1, false,
            listOf(SyncManifestItem(1, homeworkRecord(revision = 2)))))
        h.inbox.publish()
        assertEquals("pending", h.outbox.find(OP)!!.status)
        assertEquals("local", (h.outbox.payloadValue(h.outbox.find(OP)!!) as HomeworkValue).text)
    }

    @Test fun sync_reset_replaces_epoch_and_removes_only_synced_missing_records() = runBlocking {
        val h = PullHarness()
        h.initialize()
        val page = SyncChangesPage(SyncMetadata(EPOCH, 1, 0), 0, 1, false,
            listOf(SyncChange(1, OP, homeworkRecord())))
        h.inbox.applyChanges(page)
        val newEpoch = UUID.randomUUID()
        val http = FakeHttp { call ->
            when {
                call.url.contains("epoch=$EPOCH") -> jsonReply(410, errorJson(410, "sync_reset"))
                call.url.endsWith("/resync") -> jsonReply(200, manifestJson(epoch = newEpoch, highWater = 0, itemCount = 0))
                else -> jsonReply(200, changesPageJson(metadataJson(newEpoch, 0), 0, 0, false, "[]"))
            }
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pull()
        assertTrue(h.projected.isEmpty())
        assertEquals(newEpoch, h.outbox.syncEpoch)
        assertEquals(0L, h.outbox.afterSequence)
    }

    @Test fun expired_staged_manifest_is_replaced_without_publishing_old_rows() = runBlocking {
        val h = PullHarness()
        h.inbox.begin(manifest())
        val replacement = UUID.randomUUID()
        val http = FakeHttp { call ->
            when {
                call.url.contains("/resync/$MANIFEST_ID") -> jsonReply(410, errorJson(410, "manifest_expired"))
                call.url.endsWith("/resync") -> jsonReply(200, manifestJson(id = replacement, highWater = 0, itemCount = 0))
                else -> jsonReply(200, changesPageJson(metadataJson(current = 0), 0, 0, false, "[]"))
            }
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pull()
        assertNull(h.inbox.manifest)
        assertTrue(h.inbox.initialized)
        assertEquals(0L, h.outbox.afterSequence)
        assertTrue(h.projected.isEmpty())
    }

    @Test fun fresh_device_uses_snapshot_even_when_change_log_was_trimmed() = runBlocking {
        val h = PullHarness()
        val snapshot = manifestJson(highWater = 30)
        val http = FakeHttp { call ->
            when {
                call.url.endsWith("/resync") -> jsonReply(200, snapshot)
                call.url.contains("/resync/") -> jsonReply(200, resyncPageJson(manifest = snapshot,
                    items = """[{"ordinal":1,"record":${String(SyncJson.serialize(homeworkRecord(revision = 2)))}}]"""))
                else -> jsonReply(200, changesPageJson(metadataJson(current = 30, min = 20), 30, 30, false, "[]"))
            }
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pull()
        assertEquals("глава 1", (h.projected.single().value as HomeworkValue).text)
        assertEquals(30L, h.outbox.afterSequence)
        assertEquals(1, h.notifications)
    }

    @Test fun resync_is_staged_and_resumes_after_restart_without_partial_publication() = runBlocking {
        val h = PullHarness()
        val snapshot = manifestJson(itemCount = 2)
        val http = FakeHttp { call ->
            when {
                call.url.endsWith("/resync") -> jsonReply(200, snapshot)
                call.url.contains("afterOrdinal=0") -> jsonReply(200, resyncPageJson(snapshot, 0, 1, true))
                else -> throw IOException("offline")
            }
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pull()
        assertTrue(h.projected.isEmpty())
        assertNull(h.outbox.syncEpoch)
        val restarted = PrivateSyncCoordinator(h.reopenOutbox(), h.work, onApplied = { h.notifications++ })
        http.handler = { call ->
            if (call.url.contains("afterOrdinal=1")) jsonReply(200, resyncPageJson(snapshot, 1, 2, false,
                """[{"ordinal":2,"record":${String(SyncJson.serialize(homeworkRecord(revision = 2)))}}]"""))
            else if (call.url.contains("/changes?")) jsonReply(200, changesPageJson(after = 9, next = 9, hasMore = false, changes = "[]"))
            else error("must resume persisted manifest")
        }
        restarted.attach(client(http), { ACCESS }, false)
        restarted.pull()
        assertEquals(setOf("homework", "completion"), h.projected.map { it.entityType }.toSet())
        assertEquals(9L, h.reopenOutbox().afterSequence)
    }

    @Test fun changes_apply_each_exact_page_cursor_and_ignore_late_profile_response() = runBlocking {
        val h = PullHarness()
        h.initialize()
        var page = 0
        val http = FakeHttp { call ->
            page++
            if (page == 1) {
                assertTrue(call.url.contains("afterSequence=0"))
                jsonReply(200, changesPageJson(metadataJson(current = 5), 0, 3, true,
                    """[{"sequence":3,"opId":"$OP","record":${String(SyncJson.serialize(homeworkRecord(revision = 3)))}}]"""))
            } else {
                assertTrue(call.url.contains("afterSequence=3"))
                h.work.stopAccepting()
                jsonReply(200, changesPageJson(metadataJson(current = 5), 3, 5, false,
                    """[{"sequence":5,"opId":"$OP","record":${recordJson(revision = 5)}}]"""))
            }
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        h.coordinator.pull()
        assertEquals(3L, h.outbox.afterSequence)
        assertEquals("homework", h.projected.single().entityType)
    }

    @Test fun remote_changes_preserve_pending_local_and_both_conflict_values() {
        val h = PullHarness()
        h.initialize()
        h.outbox.enqueue(OP, "homework", ENTITY, 1, "upsert", homeworkValue("local"), 1)
        h.inbox.applyChanges(SyncJson.changesPage(changesPageJson(metadataJson(current = 2), 0, 2, false,
            """[{"sequence":2,"opId":"${UUID.randomUUID()}","record":${String(SyncJson.serialize(homeworkRecord(revision = 2, value = homeworkValue("remote"))))}}]""").toByteArray()))
        assertTrue(h.projected.isEmpty())
        assertEquals("local", (h.outbox.payloadValue(h.outbox.find(OP)!!) as HomeworkValue).text)
        assertEquals("remote", (h.inbox.serverRecord("homework", ENTITY)!!.value as HomeworkValue).text)
        assertEquals("conflict", h.outbox.find(OP)!!.status)
        assertEquals(2L, h.outbox.afterSequence)
    }

    @Test fun failed_commit_rolls_back_data_and_cursor_and_retry_applies_once() {
        val h = PullHarness()
        h.initialize()
        val page = SyncJson.changesPage(changesPageJson(metadataJson(current = 1), 0, 1, false).toByteArray())
        h.outbox.beforeCommit = { error("disk full") }
        assertThrows(IllegalStateException::class.java) { h.inbox.applyChanges(page) }
        assertTrue(h.projected.isEmpty())
        assertEquals(0L, h.outbox.afterSequence)
        h.outbox.beforeCommit = null
        h.inbox.applyChanges(page)
        assertEquals(1, h.projected.size)
        assertEquals(1L, h.outbox.afterSequence)
    }
}

private class PullHarness {
    val commands = MemorySyncOutboxCommands()
    val state = MemorySyncStateCommands()
    val work = ProfileWork()
    val projected = mutableListOf<SyncRecord>()
    var notifications = 0
    fun reopenOutbox() = RoomSyncOutbox(true, commands, state, transactor = { action ->
        val rows = commands.pending().toList()
        val cursor = state.get()!!
        val records = projected.toList()
        try { action() } catch (t: Throwable) {
            commands.pending().forEach { commands.delete(it.opId) }
            rows.forEach { commands.upsert(it) }
            state.upsert(cursor)
            projected.clear(); projected.addAll(records)
            throw t
        }
    }, projection = object : SyncRecordProjection {
        override fun apply(record: SyncRecord, outbox: RoomSyncOutbox): Long? {
            projected.removeAll { it.entityType == record.entityType && it.entityId == record.entityId }
            if (!record.tombstone) projected.add(record)
            return 1L
        }
    })
    val outbox = reopenOutbox()
    val inbox get() = outbox.inbox
    val coordinator = PrivateSyncCoordinator(outbox, work, onApplied = { notifications++ })
    fun initialize() {
        inbox.begin(SyncResyncManifest(MANIFEST_ID, EPOCH, 0, NOW, NOW.plusSeconds(600), 0))
        inbox.publish()
    }
}
