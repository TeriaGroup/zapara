package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.time.Duration
import java.util.concurrent.atomic.AtomicInteger

class CloudSyncStatusTest {
    private class Harness {
        val work = ProfileWork()
        val outbox = RoomSyncOutbox(true, MemorySyncOutboxCommands(), MemorySyncStateCommands())
        val coordinator = PrivateSyncCoordinator(outbox, work)
        init { outbox.inbox.begin(SyncResyncManifest(MANIFEST_ID, EPOCH, 0, NOW, NOW.plusSeconds(600), 0)); outbox.inbox.publish() }
    }
    private fun emptyChanges() = jsonReply(200, changesPageJson(metadataJson(current = 0), 0, 0, false, "[]"))

    @Test fun unavailable_pull_keeps_local_queue_and_does_not_report_success() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        val http = FakeHttp { jsonReply(503, errorJson(503, "db_unavailable")) }
        h.coordinator.attach(client(http), { ACCESS }, false)
        try {
            h.coordinator.sync()
            assertEquals(PrivateSyncState.Unavailable, h.coordinator.status.value.failure)
            assertEquals(1, h.coordinator.status.value.pending)
            assertFalse(h.coordinator.status.value.upToDate)
            assertNull(h.coordinator.status.value.lastSuccess)
            assertEquals(1, h.outbox.pending().size)
            assertEquals(1, http.requests.size)
        } finally { h.coordinator.close() }
    }

    @Test fun manual_sync_pulls_pushes_then_pulls_before_success() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        val http = FakeHttp { request ->
            if (request.method == "POST") jsonReply(200, mutationResultJson(200, "applied", metadataJson(current = 1),
                String(SyncJson.serialize(homeworkRecord())))) else emptyChanges()
        }
        h.coordinator.attach(client(http), { ACCESS }, false)
        try {
            h.coordinator.sync()
            assertEquals(listOf("GET", "POST", "GET"), http.requests.map { it.method })
            assertTrue(h.outbox.pending().isEmpty())
            assertTrue(h.coordinator.status.value.upToDate)
            assertNotNull(h.coordinator.status.value.lastSuccess)
        } finally { h.coordinator.close() }
    }

    @Test fun missing_session_is_distinguished_from_network_outage() = runBlocking {
        val h = Harness()
        val http = FakeHttp { error("No request without a session") }
        h.coordinator.attach(client(http), { null }, false)
        try {
            h.coordinator.sync()
            assertEquals(PrivateSyncState.NeedsReauthentication, h.coordinator.status.value.failure)
            assertFalse(h.coordinator.status.value.running)
            assertTrue(http.requests.isEmpty())
        } finally { h.coordinator.close() }
    }

    @Test fun local_edit_wakes_sleeping_worker_without_waiting_thirty_seconds() = runBlocking {
        val h = Harness()
        val sent = AtomicInteger()
        val http = FakeHttp { request ->
            if (request.method == "POST") {
                sent.incrementAndGet()
                jsonReply(200, mutationResultJson(200, "applied", metadataJson(current = 1),
                    String(SyncJson.serialize(homeworkRecord()))))
            } else emptyChanges()
        }
        h.coordinator.attach(client(http), { ACCESS })
        try {
            withTimeout(5_000) { h.coordinator.status.first { it.upToDate } }
            h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
            withTimeout(5_000) { h.coordinator.status.first { it.upToDate && sent.get() == 1 } }
            assertEquals(1, sent.get())
            assertTrue(h.outbox.pending().isEmpty())
        } finally { h.coordinator.close() }
    }

    @Test fun retry_after_cannot_be_bypassed_by_local_wake_or_manual_sync() = runBlocking {
        val h = Harness()
        val http = FakeHttp { jsonReply(429, errorJson(429, "rate_limited"), headers = mapOf("Retry-After" to "60")) }
        h.coordinator.attach(client(http), { ACCESS }, false)
        try {
            h.coordinator.sync()
            h.coordinator.requestSync()
            h.coordinator.sync()
            assertEquals(1, http.requests.size)
            assertEquals(PrivateSyncState.RateLimited, h.coordinator.status.value.failure)
            assertFalse(h.coordinator.status.value.upToDate)
        } finally { h.coordinator.close() }
    }

    @Test fun retired_profile_does_not_push_or_publish_success_after_pull() = runBlocking {
        val h = Harness()
        h.outbox.enqueue(OP, "homework", ENTITY, 0, "upsert", homeworkValue(), 1)
        val http = FakeHttp { h.work.stopAccepting(); emptyChanges() }
        h.coordinator.attach(client(http), { ACCESS }, false)
        try {
            h.coordinator.sync()
            assertEquals(1, http.requests.size)
            assertEquals(1, h.outbox.pending().size)
            assertFalse(h.coordinator.status.value.running)
            assertNull(h.coordinator.status.value.lastSuccess)
            assertTrue(h.work.whenIdle(500))
        } finally { h.coordinator.close() }
    }

    @Test fun failure_does_not_restart_worker_in_a_hot_loop() = runBlocking {
        val h = Harness()
        val calls = AtomicInteger()
        h.coordinator.attach(client(FakeHttp { calls.incrementAndGet(); jsonReply(503, errorJson(503, "db_unavailable")) }), { ACCESS })
        try {
            withTimeout(5_000) { h.coordinator.status.first { it.failure == PrivateSyncState.Unavailable } }
            delay(350)
            assertEquals(1, calls.get())
        } finally { h.coordinator.close() }
    }

    @Test fun monotonic_retry_window_handles_negative_clock_origin_and_never_shortens_cooldown() {
        var now = -10_000_000_000L
        val window = SyncRetryWindow { now }
        assertEquals(0L, window.remainingMillis())
        window.defer(Duration.ofSeconds(2))
        assertEquals(2000L, window.remainingMillis())
        now += 1_000_000_000L
        window.defer(Duration.ofMillis(100))
        assertEquals(1000L, window.remainingMillis())
        now += 1_000_000_000L
        assertEquals(0L, window.remainingMillis())
    }

    @Test fun applied_settings_can_reschedule_reminders_without_touching_them_for_homework_changes() = runBlocking {
        var reminderTime: String? = "07:30"
        var before: String? = null
        var rescheduled = 0
        val projection = object : SyncRecordProjection {
            override fun apply(record: SyncRecord, outbox: RoomSyncOutbox): Long? {
                if (record.value is SettingsValue) reminderTime = record.value.notifyTime1
                return 1
            }
        }
        val outbox = RoomSyncOutbox(true, MemorySyncOutboxCommands(), MemorySyncStateCommands(), projection = projection)
        outbox.inbox.begin(SyncResyncManifest(MANIFEST_ID, EPOCH, 0, NOW, NOW.plusSeconds(600), 0)); outbox.inbox.publish()
        val coordinator = PrivateSyncCoordinator(outbox, ProfileWork(), beforeApply = { before = reminderTime },
            onApplied = { if (before != reminderTime) rescheduled++ })
        var serverSequence = 1L
        val http = FakeHttp { request ->
            val after = Regex("afterSequence=(\\d+)").find(request.url)!!.groupValues[1].toLong()
            if (after == serverSequence) jsonReply(200, changesPageJson(metadataJson(current = serverSequence), after, after, false, "[]"))
            else {
                val record = if (serverSequence == 1L) homeworkRecord() else SyncRecord("settings", SyncValidation.SETTINGS_ID,
                    1, false, NOW, SettingsValue(null, false, "08:30", "20:00", 50, false))
                jsonReply(200, String(SyncJson.serialize(SyncChangesPage(SyncMetadata(EPOCH, serverSequence, 0), after,
                    serverSequence, false, listOf(SyncChange(serverSequence, java.util.UUID.randomUUID(), record))))))
            }
        }
        coordinator.attach(client(http), { ACCESS }, false)
        try {
            coordinator.sync()
            assertEquals("Unrelated homework must not cancel a pending 07:30 alarm", 0, rescheduled)
            serverSequence = 2
            coordinator.sync()
            assertEquals("08:30", reminderTime)
            assertEquals(1, rescheduled)
        } finally { coordinator.close() }
    }
}
