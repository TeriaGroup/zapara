package ru.bgtu_voenmeh.zapara.data.accounts

import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.time.Instant
import kotlinx.coroutines.CompletableDeferred

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
            assertEquals(scope.baseUri.toString() + "api/v1/auth/refresh", call.url)
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
    fun failed_refresh_stays_pending_and_is_never_resent() = runBlocking {
        val vault = vault(session(accessExpires = now.plusSeconds(30)))
        val http = FakeHttp { throw java.io.IOException("down") }
        assertFailure(AccountClientFailure.Transport) { manager(http, vault).validSession() }
        assertEquals(AccountRefreshState.Pending, vault.entry!!.refreshState)
        assertEquals(testToken("zr_", 2), vault.entry!!.session.refreshToken)
        assertFailure(AccountClientFailure.ReauthenticationRequired) { manager(http, vault).validSession() }
        assertEquals(1, http.requests.size)
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

    private fun manager(http: FakeHttp, vault: MemoryAccountSessionVault) =
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
