package ru.bgtu_voenmeh.zapara

import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSession
import ru.bgtu_voenmeh.zapara.data.accounts.AccountUser
import ru.bgtu_voenmeh.zapara.data.accounts.AccountVaultEntry
import ru.bgtu_voenmeh.zapara.data.accounts.KeystoreAccountSessionVault
import java.time.Instant
import java.util.Base64
import java.util.UUID

class AccountVaultInstrumentedTest {
    @Test
    fun keystore_round_trip_does_not_store_raw_prefs() = runBlocking {
        val ctx = InstrumentationRegistry.getInstrumentation().targetContext
        val key = "A".repeat(64)
        val vault = KeystoreAccountSessionVault(ctx, key)
        val access = tok("za_", 7)
        val refresh = tok("zr_", 8)
        val session = AccountSession(
            AccountUser(UUID.randomUUID().toString(), "Test.User", null, Instant.parse("2026-09-01T00:00:00Z")),
            UUID.randomUUID().toString(), access, refresh, "Bearer",
            Instant.parse("2026-09-08T12:00:00Z"), Instant.parse("2026-10-08T00:00:00Z")
        )
        vault.acquire().use { it.write(AccountVaultEntry.ready(key, session)) }
        vault.acquire().use { lease ->
            val read = lease.read()!!
            assertEquals(session.accessToken, read.session.accessToken)
            assertTrue(!read.toString().contains("za_"))
            lease.clear()
        }
        vault.acquire().use { assertNull(it.read()) }
        val raw = ctx.getSharedPreferences(KeystoreAccountSessionVault.PREFS, 0).all.values.joinToString()
        assertTrue(!raw.contains(access))
        assertTrue(!raw.contains(refresh))
    }

    private fun tok(prefix: String, fill: Byte): String =
        prefix + Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { fill })
}
