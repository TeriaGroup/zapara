package ru.bgtu_voenmeh.zapara.data.accounts

import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.time.Instant
import java.util.UUID

internal fun testToken(prefix: String, fill: Byte): String {
    val encoded = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { fill })
    return prefix + encoded
}

class AccountSessionVaultTest {
    private fun session(userId: String = UUID.randomUUID().toString()): AccountSession {
        val access = testToken("za_", 1)
        val refresh = testToken("zr_", 2)
        return AccountSession(
            user = AccountUser(userId, "Test.User", null, Instant.parse("2026-09-01T00:00:00Z")),
            familyId = UUID.randomUUID().toString(),
            accessToken = access,
            refreshToken = refresh,
            tokenType = "Bearer",
            accessExpiresAt = Instant.parse("2026-09-08T12:00:00Z"),
            refreshExpiresAt = Instant.parse("2026-10-08T00:00:00Z")
        )
    }

    @Test
    fun write_read_clear_round_trip_and_redacts_secrets() = runBlocking {
        val scope = AccountServerScope.parse("https://example.invalid/root")
        val vault = MemoryAccountSessionVault(scope.key)
        val entry = AccountVaultEntry.ready(scope.key, session())
        vault.acquire().use { lease ->
            assertNull(lease.read())
            lease.write(entry)
            val read = lease.read()!!
            assertEquals(entry.userId, read.userId)
            assertEquals(entry.session.accessToken, read.session.accessToken)
        }
        assertTrue(!entry.toString().contains("za_"))
        assertTrue(!entry.session.toString().contains("zr_"))
        assertTrue(!entry.toString().contains(entry.session.refreshToken))
        vault.acquire().use { it.clear() }
        vault.acquire().use { assertNull(it.read()) }
    }

    @Test
    fun rejects_entry_from_another_server_key() = runBlocking {
        val vault = MemoryAccountSessionVault("A".repeat(64))
        val other = AccountVaultEntry.ready("B".repeat(64), session())
        vault.acquire().use { lease ->
            try {
                lease.write(other)
                fail()
            } catch (e: AccountClientException) {
                assertEquals(AccountClientFailure.VaultUnavailable, e.failure)
            }
        }
    }

    @Test
    fun exclusive_lease_serializes_writers() = runBlocking {
        val vault = MemoryAccountSessionVault("A".repeat(64))
        val first = vault.acquire()
        var concurrent = false
        val job = async {
            vault.acquire().use { concurrent = true }
        }
        delay(30)
        assertFalse(concurrent)
        first.close()
        job.await()
        assertTrue(concurrent)
    }
}

class AccountValidationTest {
    @Test
    fun username_ascii_any_first_canonical_lowercase() {
        assertEquals("ab1", AccountValidation.username("ab1"))
        assertEquals("a.b_c-1", AccountValidation.username("a.b_c-1"))
        assertEquals("1user", AccountValidation.username("1user"))
        assertEquals("test.user", AccountValidation.normalizeUsername("Test.User"))
        for (bad in listOf("ab", "a".repeat(33), "абвг", "a b", "", "a@b")) {
            try {
                AccountValidation.username(bad)
                fail(bad)
            } catch (_: IllegalArgumentException) {
            }
        }
    }

    @Test
    fun password_12_128_unicode_no_trim() {
        assertEquals("password12ab", AccountValidation.password("password12ab"))
        assertEquals("  pass phrase", AccountValidation.password("  pass phrase"))
        try {
            AccountValidation.password("short")
            fail()
        } catch (_: IllegalArgumentException) {
        }
        try {
            AccountValidation.password("x".repeat(129))
            fail()
        } catch (_: IllegalArgumentException) {
        }
        try {
            AccountValidation.password("password12ab\u0000")
            fail()
        } catch (_: IllegalArgumentException) {
        }
    }

    @Test
    fun tokens_and_uuids() {
        val access = testToken("za_", 3)
        val refresh = testToken("zr_", 4)
        assertEquals(access, AccountValidation.token(access, "za_"))
        assertEquals(refresh, AccountValidation.token(refresh, "zr_"))
        try {
            AccountValidation.token(refresh, "za_")
            fail()
        } catch (_: IllegalArgumentException) {
        }
        val id = "11111111-1111-4111-8111-111111111111"
        assertEquals(id, AccountValidation.id(id))
        try {
            AccountValidation.id("00000000-0000-0000-0000-000000000000")
            fail()
        } catch (_: IllegalArgumentException) {
        }
    }

    @Test
    fun server_scope_isolates_by_origin_and_path() {
        val a = AccountServerScope.parse("https://example.invalid/root")
        val b = AccountServerScope.parse("https://example.invalid/root/")
        val c = AccountServerScope.parse("https://example.invalid/other")
        assertEquals(a.key, b.key)
        assertTrue(a.key != c.key)
        assertEquals(64, a.key.length)
        assertEquals(a.key, a.key.uppercase())
        try {
            AccountServerScope.parse("http://example.invalid/")
            fail()
        } catch (_: IllegalArgumentException) {
        }
    }
}
