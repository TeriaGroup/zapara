package ru.bgtu_voenmeh.zapara

import androidx.room.Room
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.HomeworkService
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import ru.bgtu_voenmeh.zapara.data.sync.*
import java.io.File
import java.net.URI
import java.util.UUID

/** Explicit opt-in only. Requires a backed-up local synthetic server and a private fixture file.
 * Does not touch the active application profile or print credentials. The uniquely named test
 * database is retained between roundtrip/verify phases so a second real client can participate.
 */
class LivePrivateSyncRoundtripTest {
    @Test fun real_http_room_outbox_and_coordinator_roundtrip() = runBlocking<Unit> {
        val args = InstrumentationRegistry.getArguments()
        assumeTrue("Owned local integration stand is opt-in", args.getString("liveSync") == "true")
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val fixtureFile = File(context.filesDir, "live-sync-fixture.json")
        val fixture = JSONObject(fixtureFile.readText())
        val runId = UUID.fromString(fixture.getString("runId"))
        val phase = args.getString("phase") ?: "roundtrip"
        require(phase == "roundtrip" || phase == "verify")
        val base = URI(BuildConfig.API_BASE_URL)
        require(base.scheme == "http" && base.host == "127.0.0.1")
        val transport = UrlConnectionTransport()
        val accounts = AccountHttpClient(transport, AccountServerScope.parse(base.toString()))
        val session = accounts.login(fixture.getString("username"), fixture.getString("password"),
            fixture.getString("androidDeviceId"), "Проверка Android Room")
        assertEquals(fixture.getString("userId"), session.user.userId)
        val http = PrivateSyncHttpClient(transport, base)
        val name = "private-sync-live-$runId.db"
        fun open() = Room.databaseBuilder(context, ZaparaDatabase::class.java, name).build()
        var db = open()
        var coordinator: PrivateSyncCoordinator? = null
        try {
            if (phase == "roundtrip") {
                assertTrue("Use a fresh runId for a new run", db.homeworkDao().getAll().isEmpty())
                val staged = RoomSyncOutbox.from(db, true)
                val begin = http.beginResync(session.accessToken)
                assertEquals(PrivateSyncState.Success, begin.state)
                val manifest = begin.value!!
                assertTrue("Browser must seed at least three records", manifest.itemCount >= 3)
                staged.inbox.begin(manifest)
                val first = http.readResyncPage(session.accessToken, manifest, 0, 1)
                assertEquals(PrivateSyncState.Success, first.state)
                staged.inbox.stage(first.value!!)
                assertEquals(1L, staged.inbox.afterOrdinal)
                assertTrue(db.homeworkDao().getAll().isEmpty())
                assertTrue(db.friendDao().getAll().isEmpty())
                assertNull(staged.syncEpoch)
                db.close()
                db = open()
                assertEquals(manifest, RoomSyncOutbox.from(db, true).inbox.manifest)
            }
            var outbox = RoomSyncOutbox.from(db, true)
            coordinator = PrivateSyncCoordinator(outbox, ProfileWork())
            coordinator.attach(http, { session.accessToken }, background = false)
            coordinator.sync()
            assertTrue(outbox.inbox.initialized)
            assertNull(outbox.inbox.manifest)
            for (type in listOf("homework", "friend", "settings")) {
                val id = UUID.fromString(fixture.getString("${type}Id"))
                assertNotNull("Missing browser $type", outbox.inbox.serverRecord(type, id))
            }
            assertTrue(db.homeworkDao().getAll().isNotEmpty())
            assertTrue(db.friendDao().getAll().isNotEmpty())
            if (phase == "roundtrip") {
                val repo = ScheduleRepository(db, outbox = outbox)
                val homework = HomeworkService(db.homeworkDao(), { _, _, _ -> emptyList() }, { null }, outbox)
                val added = homework.addHomework("Математика", "ANDROID tri-client offline", 2)
                val identity = outbox.identity("homework", added)!!
                val friend = repo.friends().single()
                repo.updateFriend(friend.copy(memberNames = "ANDROID tri-client friend"))
                repo.saveSettings(repo.settings().copy(notifyTime1 = "19:11"))
                val pending = outbox.pending().map { it.opId }.toSet()
                assertEquals(3, pending.size)
                coordinator.close()
                // Real socket refusal, not a fake HTTP response or a different sync algorithm.
                val offlineHttp = PrivateSyncHttpClient(transport, URI("http://127.0.0.1:1/"), timeoutMs = 1500)
                assertEquals(PrivateSyncState.Unavailable, offlineHttp.metadata(session.accessToken).state)
                coordinator = PrivateSyncCoordinator(outbox, ProfileWork())
                coordinator.attach(offlineHttp, { session.accessToken }, background = false)
                coordinator.sync()
                assertEquals(pending, outbox.pending().map { it.opId }.toSet())
                coordinator.close()
                db.close()
                db = open()
                outbox = RoomSyncOutbox.from(db, true)
                assertEquals(pending, outbox.pending().map { it.opId }.toSet())
                coordinator = PrivateSyncCoordinator(outbox, ProfileWork())
                coordinator.attach(http, { session.accessToken }, background = false)
                coordinator.sync()
                assertTrue("Acknowledged outbox must drain", outbox.pending().isEmpty())
                assertEquals(identity.entityId, outbox.identity("homework", added)!!.entityId)
                assertTrue(outbox.identity("homework", added)!!.revision > 0)
            } else {
                assertTrue(db.homeworkDao().getAll().any { it.text == "WINDOWS tri-client offline" })
                assertTrue(db.homeworkDao().getAll().any { it.text == "WINDOWS updated Android" })
                assertEquals("WINDOWS tri-client friend", db.friendDao().getAll().single().memberNames)
                assertEquals("19:22", db.settingsDao().get()!!.notifyTime1)
            }
            val evidence = JSONObject().put("phase", phase).put("userId", session.user.userId)
                .put("familyId", session.familyId).put("deviceId", fixture.getString("androidDeviceId"))
                .put("syncEpoch", outbox.syncEpoch.toString()).put("afterSequence", outbox.afterSequence)
                .put("pending", outbox.pending().size).put("database", name)
            val records = org.json.JSONArray()
            for (row in db.homeworkDao().getAll()) {
                val identity = outbox.identity("homework", row.id)!!
                records.put(JSONObject().put("entityId", identity.entityId.toString())
                    .put("revision", identity.revision).put("text", row.text))
            }
            evidence.put("homework", records)
            File(context.filesDir, "live-sync-evidence-$phase.json").writeText(evidence.toString(2))
            android.util.Log.i("ZaparaLiveSync", evidence.toString())
        } finally {
            coordinator?.close()
            db.close()
        }
    }
}
