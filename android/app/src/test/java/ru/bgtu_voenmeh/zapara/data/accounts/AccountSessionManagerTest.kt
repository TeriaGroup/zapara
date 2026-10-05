package ru.bgtu_voenmeh.zapara.data.accounts

import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.cancelAndJoin

class AccountSessionManagerTest {
    private val scope = AccountServerScope.parse("https://example.invalid/root")
    private val userId = "11111111-1111-4111-8111-111111111111"
    private val familyId = "22222222-2222-4222-8222-222222222222"
    private val created = Instant.parse("2026-09-01T00:00:00Z")
    private val now = Instant.parse("2026-09-08T12:00:00Z")
    private val refreshExpires = Instant.parse("2026-10-08T00:00:00Z")

    @Test
    fun fresh_access_is_returned_without_network() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(31)))
        val http = FakeHttp { error("network") }
        val session = manager(http, vault).validSession()
        assertEquals(testToken("za_", 1), session.accessToken)
        assertEquals(0, http.requests.size)
    }

    @Test
    fun near_expiry_posts_one_refresh_and_stores_the_new_pair() = runBlocking {
        val current = session(accessExpires = now.plusSeconds(30))
        val vault = vault(current)
        val nextAccess = testToken("za_", 9)
        val nextRefresh = testToken("zr_", 9)
        val http = FakeHttp { call ->
            assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
            assertEquals(current.refreshToken, vault.entry!!.session.refreshToken)
            assertEquals("POST", call.method)
            assertEquals(scope.baseUri.toString() + "api/v2/auth/refresh", call.url)
            assertEquals(vault.entry!!.refreshAttemptId, call.headers["X-Zapara-Refresh-Attempt"])
            assertNotNull(vault.entry!!.refreshAttemptId)
            assertNull(call.headers["Authorization"])
            assertEquals("""{"refreshToken":"${current.refreshToken}"}""", String(call.body!!))
            json(session(access = nextAccess, refresh = nextRefresh, accessExpires = now.plusSeconds(900)))
        }
        val session = manager(http, vault).validSession()
        assertEquals(nextAccess, session.accessToken)
        assertEquals(nextRefresh, session.refreshToken)
        assertEquals(AccountRefreshState.Ready, vault.entry!!.refreshState)
        assertEquals(nextAccess, vault.entry!!.session.accessToken)
        assertEquals(1, http.requests.size)
        val again = manager(http, vault).validSession()
        assertEquals(nextAccess, again.accessToken)
        assertEquals(1, http.requests.size)
    }

    @Test
    fun lost_response_retries_same_attempt_after_restart_and_stores_new_pair() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val http = FakeHttp { throw java.io.IOException("down") }
        assertFailure(AccountClientFailure.Transport) { manager(http, vault).validSession() }
        assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
        assertEquals(testToken("zr_", 2), vault.entry!!.session.refreshToken)
        val attemptId = vault.entry!!.refreshAttemptId
        assertNotNull(attemptId)
        val persisted = AccountVaultEntry.decode(vault.entry!!.encode(), scope.key)
        vault.acquire().use { it.write(persisted) }
        http.handler = { call ->
            assertEquals(attemptId, call.headers["X-Zapara-Refresh-Attempt"])
            json(session(access = testToken("za_", 9), refresh = testToken("zr_", 9), accessExpires = now.plusSeconds(900)))
        }
        assertEquals(testToken("za_", 9), manager(http, vault).validSession().accessToken)
        assertEquals(AccountRefreshState.Ready, vault.entry!!.refreshState)
        assertNull(vault.entry!!.refreshAttemptId)
        assertEquals(2, http.requests.size)
    }

    @Test
    fun old_server_404_falls_back_once_to_legacy_refresh() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val http = FakeHttp { call ->
            if (call.url.endsWith("api/v2/auth/refresh")) {
                HttpReply(404, "".toByteArray())
            } else {
                assertEquals(scope.baseUri.toString() + "api/v1/auth/refresh", call.url)
                assertNull(call.headers["X-Zapara-Refresh-Attempt"])
                assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
                assertNull(vault.entry!!.refreshAttemptId)
                json(session(access = testToken("za_", 9), refresh = testToken("zr_", 9), accessExpires = now.plusSeconds(900)))
            }
        }
        assertEquals(testToken("za_", 9), manager(http, vault).validSession().accessToken)
        assertEquals(2, http.requests.size)
    }

    @Test
    fun ambiguous_legacy_failure_is_not_retried() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val http = FakeHttp { call ->
            if (call.url.endsWith("api/v2/auth/refresh")) HttpReply(404, ByteArray(0))
            else throw java.io.IOException("lost legacy response")
        }
        assertFailure(AccountClientFailure.Transport) { manager(http, vault).validSession() }
        assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
        assertNull(vault.entry!!.refreshAttemptId)
        assertFailure(AccountClientFailure.ReauthenticationRequired) { manager(http, vault).validSession() }
        assertEquals(2, http.requests.size)
    }

    @Test
    fun recovered_expired_access_rotates_again_with_new_attempt_and_refresh_token() = runBlocking {
        val old = session(accessExpires = now.minusSeconds(60))
        val vault = vault(old)
        val recoveredAttempt = "12345678-1234-4234-8234-123456789abc"
        vault.acquire().use { it.write(vault.entry!!.copy(
            refreshState = AccountRefreshState.Pending, refreshAttemptId = recoveredAttempt
        )) }
        val recovered = session(
            access = testToken("za_", 9), refresh = testToken("zr_", 9),
            accessExpires = now.minusSeconds(1)
        )
        val fresh = session(
            access = testToken("za_", 10), refresh = testToken("zr_", 10),
            accessExpires = now.plusSeconds(900)
        )
        val http = FakeHttp { call ->
            when (call.headers["X-Zapara-Refresh-Attempt"]) {
                recoveredAttempt -> {
                    assertEquals(old.refreshToken, String(call.body!!).substringAfter("\"refreshToken\":\"").substringBefore('"'))
                    json(recovered)
                }
                else -> {
                    assertEquals(recovered.refreshToken, String(call.body!!).substringAfter("\"refreshToken\":\"").substringBefore('"'))
                    assertEquals(recovered.refreshToken, vault.entry!!.session.refreshToken)
                    json(fresh)
                }
            }
        }
        assertEquals(fresh.accessToken, manager(http, vault).validSession().accessToken)
        assertEquals(fresh.refreshToken, vault.entry!!.session.refreshToken)
        assertEquals(AccountRefreshState.Ready, vault.entry!!.refreshState)
        assertEquals(2, http.requests.size)
        assertEquals(recoveredAttempt, http.requests[0].headers["X-Zapara-Refresh-Attempt"])
        org.junit.Assert.assertNotEquals(recoveredAttempt, http.requests[1].headers["X-Zapara-Refresh-Attempt"])
    }

    @Test
    fun v2_session_not_found_does_not_fall_back_to_v1() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val http = FakeHttp { HttpReply(404, """{"code":"session_not_found"}""".toByteArray()) }
        assertFailure(AccountClientFailure.SessionNotFound) { manager(http, vault).validSession() }
        assertEquals(1, http.requests.size)
        assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
        assertNull(vault.entry!!.refreshAttemptId)
        assertFailure(AccountClientFailure.ReauthenticationRequired) { manager(http, vault).validSession() }
        assertEquals(1, http.requests.size)
    }

    @Test
    fun server_unavailable_keeps_retryable_pending_attempt() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val http = FakeHttp { HttpReply(503, """{"code":"db_unavailable"}""".toByteArray()) }
        assertFailure(AccountClientFailure.DbUnavailable) { manager(http, vault).validSession() }
        val attemptId = vault.entry!!.refreshAttemptId
        assertNotNull(attemptId)
        assertFailure(AccountClientFailure.DbUnavailable) { manager(http, vault).validSession() }
        assertEquals(attemptId, vault.entry!!.refreshAttemptId)
        assertEquals(2, http.requests.size)
    }

    @Test
    fun failed_ready_write_retries_same_attempt_after_server_success() = runBlocking {
        val backing = vault(session(accessExpires = now.plusSeconds(30)))
        var failReadyWrite = true
        val vault = object : AccountSessionVault {
            override val serverKey = backing.serverKey
            override suspend fun acquire(): AccountVaultLease {
                val lease = backing.acquire()
                return object : AccountVaultLease {
                    override fun read() = lease.read()
                    override fun write(entry: AccountVaultEntry) {
                        if (entry.refreshState == AccountRefreshState.Ready && failReadyWrite) {
                            failReadyWrite = false
                            throw AccountClientException(AccountClientFailure.VaultUnavailable)
                        }
                        lease.write(entry)
                    }
                    override fun clear() = lease.clear()
                    override fun close() = lease.close()
                }
            }
        }
        val next = session(access = testToken("za_", 9), refresh = testToken("zr_", 9), accessExpires = now.plusSeconds(900))
        val http = FakeHttp { json(next) }
        assertFailure(AccountClientFailure.VaultUnavailable) { manager(http, vault).validSession() }
        assertEquals(AccountRefreshState.Pending, backing.entry!!.refreshState)
        val attemptId = backing.entry!!.refreshAttemptId
        assertNotNull(attemptId)
        assertEquals(next.accessToken, manager(http, vault).validSession().accessToken)
        assertEquals(attemptId, http.requests[1].headers["X-Zapara-Refresh-Attempt"])
        assertEquals(AccountRefreshState.Ready, backing.entry!!.refreshState)
    }

    @Test
    fun cancellation_keeps_pending_attempt_for_next_call() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val gate = CompletableDeferred<Unit>()
        val http = FakeHttp {
            gate.await()
            json(session(access = testToken("za_", 9), refresh = testToken("zr_", 9), accessExpires = now.plusSeconds(900)))
        }
        val job = async { manager(http, vault).validSession() }
        while (http.requests.isEmpty()) delay(1)
        val attemptId = vault.entry!!.refreshAttemptId
        job.cancelAndJoin()
        assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
        assertEquals(attemptId, vault.entry!!.refreshAttemptId)
        gate.complete(Unit)
        assertEquals(testToken("za_", 9), manager(http, vault).validSession().accessToken)
        assertEquals(attemptId, http.requests[1].headers["X-Zapara-Refresh-Attempt"])
    }

    @Test
    fun mismatched_or_unchanged_refresh_stays_pending() = runBlocking {
        for (kind in listOf("family", "user", "expiry", "same")) {
            val current = session(accessExpires = now.plusSeconds(30))
            val vault = vault(current)
            val http = FakeHttp {
                json(when (kind) {
                    "family" -> session(accessExpires = now.plusSeconds(900), family = "33333333-3333-4333-8333-333333333333", access = testToken("za_", 9), refresh = testToken("zr_", 9))
                    "user" -> session(accessExpires = now.plusSeconds(900), user = "33333333-3333-4333-8333-333333333333", access = testToken("za_", 9), refresh = testToken("zr_", 9))
                    "expiry" -> session(accessExpires = now.plusSeconds(900), refreshExpires = refreshExpires.plusSeconds(86400), access = testToken("za_", 9), refresh = testToken("zr_", 9))
                    else -> session(accessExpires = now.plusSeconds(900))
                })
            }
            assertFailure(AccountClientFailure.InvalidPayload) { manager(http, vault).validSession() }
            assertEquals(kind, AccountRefreshState.Pending, vault.entry!!.refreshState)
            assertEquals(current.refreshToken, vault.entry!!.session.refreshToken)
        }
    }

    @Test
    fun refresh_accepts_microsecond_truncation_of_existing_family_expiry() = runBlocking {
        for (truncationNanos in listOf(100L, 900L, 1_000L)) {
            val current = session(
                accessExpires = now.plusSeconds(30),
                refreshExpires = refreshExpires.plusNanos(truncationNanos)
            )
            val vault = vault(current)
            val next = session(
                access = testToken("za_", 9), refresh = testToken("zr_", 9),
                accessExpires = now.plusSeconds(900), refreshExpires = refreshExpires
            )
            val http = FakeHttp { json(next) }
            assertEquals(next.refreshToken, manager(http, vault).validSession().refreshToken)
            assertEquals(next.refreshExpiresAt, vault.entry!!.session.refreshExpiresAt)
            assertEquals(AccountRefreshState.Ready, vault.entry!!.refreshState)
        }
    }

    @Test
    fun refresh_rejects_any_family_expiry_extension_or_truncation_over_one_microsecond() = runBlocking {
        for ((name, oldExpiry, nextExpiry) in listOf(
            Triple("extension", refreshExpires, refreshExpires.plusNanos(1)),
            Triple("excess truncation", refreshExpires.plusNanos(1001), refreshExpires)
        )) {
            val vault = vault(session(accessExpires = now.plusSeconds(30), refreshExpires = oldExpiry))
            val http = FakeHttp {
                json(session(
                    access = testToken("za_", 9), refresh = testToken("zr_", 9),
                    accessExpires = now.plusSeconds(900), refreshExpires = nextExpiry
                ))
            }
            assertFailure(AccountClientFailure.InvalidPayload) { manager(http, vault).validSession() }
            assertEquals(name, AccountRefreshState.Pending, vault.entry!!.refreshState)
        }
    }

    @Test
    fun pending_vault_requires_reauthentication() = runBlocking {
        val ready = AccountVaultEntry.ready(scope.key, session(accessExpires = now.plusSeconds(900)))
        val vault = MemoryAccountSessionVault(scope.key)
        vault.acquire().use { it.write(ready.copy(refreshState = AccountRefreshState.Pending)) }
        val http = FakeHttp { error("network") }
        assertFailure(AccountClientFailure.ReauthenticationRequired) { manager(http, vault).validSession() }
        assertEquals(0, http.requests.size)
    }

    @Test
    fun one_in_flight_refresh_serves_the_waiter() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val gate = CompletableDeferred<Unit>()
        val nextAccess = testToken("za_", 9)
        val http = FakeHttp {
            gate.await()
            json(session(access = nextAccess, refresh = testToken("zr_", 9), accessExpires = now.plusSeconds(900)))
        }
        val sessions = manager(http, vault)
        val first = async { sessions.validSession() }
        while (vault.entry?.refreshState != AccountRefreshState.Pending) delay(1)
        val second = async { sessions.validSession() }
        delay(20)
        gate.complete(Unit)
        assertEquals(nextAccess, first.await().accessToken)
        assertEquals(nextAccess, second.await().accessToken)
        assertEquals(1, http.requests.size)
    }

    private fun manager(http: FakeHttp, vault: AccountSessionVault) =
        AccountSessionManager(AccountHttpClient(http, scope), vault) { now }

    private suspend fun vault(session: AccountSession): MemoryAccountSessionVault {
        val vault = MemoryAccountSessionVault(scope.key)
        vault.acquire().use { it.write(AccountVaultEntry.ready(scope.key, session)) }
        return vault
    }

    private fun session(
        access: String = testToken("za_", 1),
        refresh: String = testToken("zr_", 2),
        accessExpires: Instant,
        refreshExpires: Instant = this.refreshExpires,
        user: String = userId,
        family: String = familyId
    ) = AccountSession(
        user = AccountUser(user, "Test.User", null, created),
        familyId = family,
        accessToken = access,
        refreshToken = refresh,
        tokenType = "Bearer",
        accessExpiresAt = accessExpires,
        refreshExpiresAt = refreshExpires
    )

    private fun json(session: AccountSession) = HttpReply(
        200,
        """{"user":{"userId":"${session.user.userId}","username":"${session.user.username}","displayName":null,"createdAt":"${session.user.createdAt}"},
            |"familyId":"${session.familyId}","accessToken":"${session.accessToken}","refreshToken":"${session.refreshToken}",
            |"tokenType":"Bearer","accessExpiresAt":"${session.accessExpiresAt}","refreshExpiresAt":"${session.refreshExpiresAt}"}"""
            .trimMargin().toByteArray(),
        "application/json"
    )

    private suspend fun assertFailure(failure: AccountClientFailure, block: suspend () -> Unit) {
        try {
            block()
            fail(failure.name)
        } catch (e: AccountClientException) {
            assertEquals(failure, e.failure)
        }
    }
}
