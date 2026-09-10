package ru.bgtu_voenmeh.zapara.data.sync

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.db.SyncStateEntity
import java.util.UUID

class RoomSyncStartupTest {
    private val epoch = UUID.fromString("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")

    @Test fun constructing_guest_and_account_outboxes_does_not_access_storage() {
        for (enabled in listOf(false, true)) {
            var readsAllowed = false
            val state = object : SyncStateCommands {
                override fun get(): SyncStateEntity {
                    check(readsAllowed) { "Storage queried on the application construction thread" }
                    return SyncStateEntity(1, epoch.toString(), 17)
                }
                override fun upsert(row: SyncStateEntity) = Unit
            }
            val outbox = RoomSyncOutbox(enabled, MemorySyncOutboxCommands(), state)
            readsAllowed = true // Sync operations run later on the coordinator's IO dispatcher.
            assertEquals(epoch, outbox.syncEpoch)
            assertEquals(17L, outbox.afterSequence)
        }
    }

    @Test fun epoch_and_sequence_are_loaded_from_one_persisted_snapshot() {
        var reads = 0
        val state = object : SyncStateCommands {
            override fun get(): SyncStateEntity =
                SyncStateEntity(1, epoch.toString(), (++reads).toLong())
            override fun upsert(row: SyncStateEntity) = Unit
        }
        val outbox = RoomSyncOutbox(true, MemorySyncOutboxCommands(), state)
        assertEquals(epoch, outbox.syncEpoch)
        assertEquals(1L, outbox.afterSequence)
        assertEquals(1L, outbox.afterSequence)
        assertEquals(1, reads)
    }

    @Test fun changed_cursor_survives_recreating_the_outbox() {
        val state = MemorySyncStateCommands()
        val outbox = RoomSyncOutbox(true, MemorySyncOutboxCommands(), state)
        outbox.setEpoch(epoch, 23)
        assertEquals(epoch, outbox.syncEpoch)
        assertEquals(23L, outbox.afterSequence)
        val reopened = RoomSyncOutbox(true, MemorySyncOutboxCommands(), state)
        assertEquals(epoch, reopened.syncEpoch)
        assertEquals(23L, reopened.afterSequence)
    }

    @Test fun failed_cursor_write_keeps_the_previous_cursor() {
        val state = object : SyncStateCommands {
            override fun get() = SyncStateEntity(1, epoch.toString(), 17)
            override fun upsert(row: SyncStateEntity): Unit = error("write failed")
        }
        val outbox = RoomSyncOutbox(true, MemorySyncOutboxCommands(), state)
        assertThrows(IllegalStateException::class.java) { outbox.setEpoch(UUID.randomUUID(), 99) }
        assertEquals(epoch, outbox.syncEpoch)
        assertEquals(17L, outbox.afterSequence)
    }
}
