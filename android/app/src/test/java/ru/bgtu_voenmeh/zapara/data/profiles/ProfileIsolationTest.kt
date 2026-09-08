package ru.bgtu_voenmeh.zapara.data.profiles

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSession
import ru.bgtu_voenmeh.zapara.data.accounts.AccountUser
import ru.bgtu_voenmeh.zapara.data.accounts.AccountVaultEntry
import ru.bgtu_voenmeh.zapara.data.accounts.MemoryAccountSessionVault
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.MemoryTimetableStore
import java.time.Instant
import java.util.UUID

class ProfileDescriptorTest {
    @Test
    fun guest_keeps_zapara_db_name() {
        val guest = ProfileDescriptor.guest()
        assertTrue(guest.isGuest)
        assertEquals("zapara.db", guest.databaseName)
        assertNull(guest.userId)
    }

    @Test
    fun account_uses_validated_server_uuid() {
        val key = "A".repeat(64)
        val user = "11111111-1111-4111-8111-111111111111"
        val account = ProfileDescriptor.account(key, user)
        assertFalse(account.isGuest)
        assertEquals("profiles/$key/$user/zapara.db", account.databaseName)
        try {
            ProfileDescriptor.account("aa", user)
            fail()
        } catch (_: IllegalArgumentException) {
        }
        try {
            ProfileDescriptor.account(key, "00000000-0000-0000-0000-000000000000")
            fail()
        } catch (_: IllegalArgumentException) {
        }
        try {
            ProfileDescriptor.account(key.lowercase(), user)
            fail()
        } catch (_: IllegalArgumentException) {
        }
    }
}

class ProfileWorkTest {
    @Test
    fun late_results_rejected_after_stop() = runBlocking {
        val work = ProfileWork()
        val ticket = work.enter()
        assertTrue(ticket.isCurrent)
        val committed = CompletableDeferred<Boolean>()
        val started = CompletableDeferred<Unit>()
        val job = async {
            started.complete(Unit)
            delay(80)
            if (ticket.isCurrent) committed.complete(true) else committed.complete(false)
        }
        started.await()
        work.stopAccepting()
        assertFalse(ticket.isCurrent)
        try {
            ticket.throwIfStale()
            fail()
        } catch (_: kotlinx.coroutines.CancellationException) {
        }
        job.await()
        assertFalse(committed.await())
        work.resume()
        val next = work.enter()
        assertTrue(next.isCurrent)
        next.close()
    }

    @Test
    fun drain_waits_for_outstanding_then_allows_close() = runBlocking {
        val work = ProfileWork()
        val ticket = work.enter()
        val release = CompletableDeferred<Unit>()
        val job = async {
            try {
                release.await()
            } finally {
                ticket.close()
            }
        }
        work.stopAccepting()
        val drained = async { work.whenIdle(1_000) }
        delay(20)
        assertFalse(drained.isCompleted)
        release.complete(Unit)
        job.await()
        assertTrue(drained.await())
    }
}

class ProfileCoordinatorTest {
    private fun session(userId: String): AccountSession {
        return AccountSession(
            user = AccountUser(userId, "Test.User", null, Instant.parse("2026-09-01T00:00:00Z")),
            familyId = UUID.randomUUID().toString(),
            accessToken = testToken("za_", 5),
            refreshToken = testToken("zr_", 6),
            tokenType = "Bearer",
            accessExpiresAt = Instant.parse("2026-09-08T12:00:00Z"),
            refreshExpiresAt = Instant.parse("2026-10-08T00:00:00Z")
        )
    }

    @Test
    fun guest_account_guest_keep_distinct_stores() = runBlocking {
        val scope = AccountServerScope.parse("https://example.invalid/root")
        val vault = MemoryAccountSessionVault(scope.key)
        val stores = mutableMapOf<String, MemoryTimetableStore>()
        val graphs = mutableMapOf<String, ProfileGraph>()
        fun open(desc: ProfileDescriptor): ProfileGraph {
            val existing = graphs[desc.databaseName]
            if (existing != null && !existing.closed) error("already open")
            val store = stores.getOrPut(desc.databaseName) { MemoryTimetableStore() }
            val g = ProfileGraph(desc, store, ProfileWork())
            graphs[desc.databaseName] = g
            return g
        }
        val guest = open(ProfileDescriptor.guest())
        guest.store.saveSettings(guest.store.settings().copy(myGroupId = "guest"))
        val coordinator = ProfileCoordinator(guest, ::open, vault)
        val userA = "11111111-1111-4111-8111-111111111111"
        val userB = "22222222-2222-4222-8222-222222222222"
        assertTrue(coordinator.commitSession(session(userA), scope.key).committed)
        coordinator.current.store.saveSettings(coordinator.current.store.settings().copy(myGroupId = "A"))
        assertNotEquals("guest", coordinator.current.store.settings().myGroupId)
        assertTrue(graphs.getValue("zapara.db").closed)
        assertTrue(coordinator.commitSession(session(userB), scope.key).committed)
        coordinator.current.store.saveSettings(coordinator.current.store.settings().copy(myGroupId = "B"))
        assertTrue(coordinator.logout().committed)
        assertEquals("guest", coordinator.current.store.settings().myGroupId)
        assertEquals("guest", graphs.getValue("zapara.db").store.settings().myGroupId)
        val cachedA = open(ProfileDescriptor.account(scope.key, userA))
        assertEquals("A", cachedA.store.settings().myGroupId)
        cachedA.close()
        val cachedB = open(ProfileDescriptor.account(scope.key, userB))
        assertEquals("B", cachedB.store.settings().myGroupId)
        cachedB.close()
        vault.acquire().use { assertNull(it.read()) }
        coordinator.close()
    }

    @Test
    fun busy_gate_rolls_back_without_activating_vault() = runBlocking {
        val scope = AccountServerScope.parse("http://127.0.0.1/profile/")
        val vault = MemoryAccountSessionVault(scope.key)
        val guest = ProfileGraph(ProfileDescriptor.guest(), MemoryTimetableStore(), ProfileWork())
        val ticket = guest.work.enter()
        val coordinator = ProfileCoordinator(guest, { desc ->
            ProfileGraph(desc, MemoryTimetableStore(), ProfileWork())
        }, vault, idleTimeoutMs = 40)
        val result = coordinator.commitSession(
            session("11111111-1111-4111-8111-111111111111"),
            scope.key
        )
        assertFalse(result.committed)
        assertTrue(coordinator.current.descriptor.isGuest)
        vault.acquire().use { assertNull(it.read()) }
        ticket.close()
        coordinator.close()
    }

    @Test
    fun restore_descriptor_uses_vault_account_not_guest() = runBlocking {
        val scope = AccountServerScope.parse("https://example.invalid/root")
        val vault = MemoryAccountSessionVault(scope.key)
        val user = "11111111-1111-4111-8111-111111111111"
        vault.acquire().use { it.write(AccountVaultEntry.ready(scope.key, session(user))) }
        val desc = ProfileRestore.descriptor(vault)
        assertFalse(desc.isGuest)
        assertEquals(user, desc.userId)
        assertEquals(scope.key, desc.serverKey)
    }

    @Test
    fun logout_posts_then_clears_and_keeps_local_on_transport_failure() = runBlocking {
        val scope = AccountServerScope.parse("https://example.invalid/root")
        val vault = MemoryAccountSessionVault(scope.key)
        val guest = ProfileGraph(ProfileDescriptor.guest(), MemoryTimetableStore(), ProfileWork())
        val coordinator = ProfileCoordinator(guest, { desc ->
            ProfileGraph(desc, MemoryTimetableStore(), ProfileWork())
        }, vault)
        val user = "11111111-1111-4111-8111-111111111111"
        assertTrue(coordinator.commitSession(session(user), scope.key).committed)
        var posted = 0
        assertTrue(coordinator.logout { posted++ }.committed)
        assertEquals(1, posted)
        vault.acquire().use { assertNull(it.read()) }
        assertTrue(coordinator.current.descriptor.isGuest)
        assertTrue(coordinator.commitSession(session(user), scope.key).committed)
        assertTrue(coordinator.logout { throw java.io.IOException("down") }.committed)
        vault.acquire().use { assertNull(it.read()) }
        coordinator.close()
    }

    @Test
    fun drain_waits_for_inflight_api_refresh() = runBlocking {
        val arrived = CompletableDeferred<Unit>()
        val release = CompletableDeferred<Unit>()
        val http = ru.bgtu_voenmeh.zapara.data.api.FakeHttp { _ ->
            arrived.complete(Unit)
            release.await()
            ru.bgtu_voenmeh.zapara.data.api.jsonReply(ru.bgtu_voenmeh.zapara.data.api.catalogJson())
        }
        val store = MemoryTimetableStore()
        val work = ProfileWork()
        val api = ru.bgtu_voenmeh.zapara.data.api.ApiRefreshCoordinator(store, work, "https://example.invalid/", http)
        val vault = MemoryAccountSessionVault("A".repeat(64))
        val guest = ProfileGraph(ProfileDescriptor.guest(), store, work)
        val coordinator = ProfileCoordinator(guest, { desc ->
            ProfileGraph(desc, MemoryTimetableStore(), ProfileWork())
        }, vault, idleTimeoutMs = 80)
        val refresh = async { api.refresh() }
        arrived.await()
        val result = coordinator.commitSession(session("11111111-1111-4111-8111-111111111111"), "A".repeat(64))
        assertFalse(result.committed)
        assertTrue(coordinator.current.descriptor.isGuest)
        release.complete(Unit)
        refresh.await()
        coordinator.close()
    }

    @Test
    fun vault_write_uses_keystore_envelope_not_raw_token_store() {
        val scope = AccountServerScope.parse("https://example.invalid/root")
        val entry = AccountVaultEntry.ready(scope.key, session("11111111-1111-4111-8111-111111111111"))
        val encoded = entry.encode()
        assertTrue(encoded.contains("\"version\":1"))
        assertTrue(!encoded.contains("rawToken"))
        val decoded = AccountVaultEntry.decode(encoded, scope.key)
        assertEquals(entry.session.accessToken, decoded.session.accessToken)
    }
}
