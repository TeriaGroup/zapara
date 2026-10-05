package ru.bgtu_voenmeh.zapara

import androidx.room.Room
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.sync.*
import java.time.Instant
import java.util.UUID

/** Uses only a uniquely named synthetic test database, never the user's active profile. */
class PrivateSyncRoomTest {
    @Test fun staged_snapshot_survives_reopen_and_failed_publication_rolls_back_room_and_cursor() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val name = "private-sync-test-${UUID.randomUUID()}.db"
        fun open() = Room.databaseBuilder(context, ZaparaDatabase::class.java, name).build()
        val epoch = UUID.randomUUID()
        val entity = UUID.randomUUID()
        val now = Instant.parse("2026-09-20T22:00:00Z")
        val manifest = SyncResyncManifest(UUID.randomUUID(), epoch, 4, now, now.plusSeconds(600), 1)
        val record = SyncRecord("homework", entity, 4, false, now,
            HomeworkValue("Математика", "математика", "Задание с другого устройства", 2, now, null))
        var db = open()
        try {
            val first = RoomSyncOutbox.from(db, true)
            first.inbox.begin(manifest)
            first.inbox.stage(SyncResyncPage(manifest, 0, 1, false, listOf(SyncManifestItem(1, record))))
            assertTrue(db.homeworkDao().getAll().isEmpty())
            assertNull(first.syncEpoch)
            db.close()
            db = open()
            val resumed = RoomSyncOutbox.from(db, true)
            assertEquals(manifest, resumed.inbox.manifest)
            assertEquals(1L, resumed.inbox.afterOrdinal)
            resumed.beforeCommit = { error("simulated storage failure") }
            assertThrows(IllegalStateException::class.java) { resumed.inbox.publish() }
            assertTrue(db.homeworkDao().getAll().isEmpty())
            assertNull(resumed.syncEpoch)
            assertEquals(manifest, resumed.inbox.manifest)
            resumed.beforeCommit = null
            resumed.inbox.publish()
            assertEquals("Задание с другого устройства", db.homeworkDao().getAll().single().text)
            assertEquals(4L, resumed.afterSequence)
            assertEquals(4L, db.syncStateDao().get()!!.afterSequence)
            assertTrue(resumed.pending().isEmpty())
            assertNull(resumed.inbox.manifest)
        } finally {
            db.close()
            context.deleteDatabase(name)
        }
    }
}
