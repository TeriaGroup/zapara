package ru.bgtu_voenmeh.zapara.data.accounts

import ru.bgtu_voenmeh.zapara.data.api.TimetableApiClient
import java.net.URI
import java.security.MessageDigest
import java.util.Locale

class AccountServerScope private constructor(val baseUri: URI, val key: String) {
    override fun toString(): String = "AccountServerScope"

    companion object {
        fun parse(url: String): AccountServerScope {
            val uri = try {
                URI(url)
            } catch (_: Exception) {
                throw IllegalArgumentException("Некорректный адрес API.")
            }
            val canonical = TimetableApiClient.validateBaseUri(uri)
            val digest = MessageDigest.getInstance("SHA-256").digest(canonical.toString().toByteArray(Charsets.UTF_8))
            val key = digest.joinToString("") { b -> "%02X".format(b) }
            return AccountServerScope(canonical, key)
        }
    }
}

interface AccountVaultLease : AutoCloseable {
    fun read(): AccountVaultEntry?
    fun write(entry: AccountVaultEntry)
    fun clear()
}

interface AccountSessionVault {
    val serverKey: String
    suspend fun acquire(): AccountVaultLease
}

class MemoryAccountSessionVault(override val serverKey: String) : AccountSessionVault {
    private val mutex = kotlinx.coroutines.sync.Mutex()
    internal var entry: AccountVaultEntry? = null

    override suspend fun acquire(): AccountVaultLease {
        mutex.lock()
        return object : AccountVaultLease {
            override fun read(): AccountVaultEntry? = entry
            override fun write(entry: AccountVaultEntry) {
                if (entry.serverKey != serverKey || entry.version != 1) {
                    throw AccountClientException(AccountClientFailure.VaultUnavailable)
                }
                this@MemoryAccountSessionVault.entry = entry
            }
            override fun clear() {
                entry = null
            }
            override fun close() {
                mutex.unlock()
            }
        }
    }
}
