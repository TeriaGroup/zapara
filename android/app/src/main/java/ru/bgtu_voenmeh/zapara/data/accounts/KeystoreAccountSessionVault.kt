package ru.bgtu_voenmeh.zapara.data.accounts

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import kotlinx.coroutines.sync.Mutex

class KeystoreAccountSessionVault(
    context: Context,
    override val serverKey: String
) : AccountSessionVault {
    private val mutex = Mutex()
    private val prefs: SharedPreferences = EncryptedSharedPreferences.create(
        PREFS,
        MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC),
        context.applicationContext,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    override suspend fun acquire(): AccountVaultLease {
        mutex.lock()
        return object : AccountVaultLease {
            override fun read(): AccountVaultEntry? {
                val json = prefs.getString(KEY, null) ?: return null
                return AccountVaultEntry.decode(json, serverKey)
            }

            override fun write(entry: AccountVaultEntry) {
                if (entry.serverKey != serverKey || entry.version != 1) {
                    throw AccountClientException(AccountClientFailure.VaultUnavailable)
                }
                val encoded = entry.encode()
                if (!prefs.edit().putString(KEY, encoded).commit()) {
                    throw AccountClientException(AccountClientFailure.VaultUnavailable)
                }
            }

            override fun clear() {
                if (!prefs.edit().remove(KEY).commit()) {
                    throw AccountClientException(AccountClientFailure.VaultUnavailable)
                }
            }

            override fun close() {
                mutex.unlock()
            }
        }
    }

    companion object {
        const val PREFS = "zapara_account_vault"
        private const val KEY = "entry"
    }
}
