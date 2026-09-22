package ru.bgtu_voenmeh.zapara.data.accounts

import java.time.Instant

/**
 * One-shot refresh. A failed rotation leaves the vault Pending so the old zr_ is never sent again.
 */
class AccountSessionManager(
    private val client: AccountHttpClient,
    private val vault: AccountSessionVault,
    private val clock: () -> Instant = { Instant.now() }
) {
    init {
        if (client.scope.key != vault.serverKey) {
            throw AccountClientException(AccountClientFailure.VaultUnavailable)
        }
    }

    suspend fun validSession(): AccountSession {
        vault.acquire().use { lease ->
            val current = ready(lease.read())
            if (current.session.accessExpiresAt.isAfter(clock().plusSeconds(30))) return current.session
            return rotate(lease, current)
        }
    }

    private suspend fun rotate(lease: AccountVaultLease, current: AccountVaultEntry): AccountSession {
        if (!current.session.refreshExpiresAt.isAfter(clock())) throw reauth()
        lease.write(current.copy(refreshState = AccountRefreshState.Pending))
        val next = client.refresh(current.session.refreshToken)
        if (!acceptable(current.session, next)) {
            throw AccountClientException(AccountClientFailure.InvalidPayload)
        }
        lease.write(AccountVaultEntry.ready(vault.serverKey, next))
        return next
    }

    private fun acceptable(old: AccountSession, next: AccountSession): Boolean {
        if (next.user.userId != old.user.userId || next.familyId != old.familyId) return false
        if (next.user.createdAt != old.user.createdAt || next.refreshExpiresAt != old.refreshExpiresAt) return false
        if (next.accessToken == old.accessToken || next.refreshToken == old.refreshToken) return false
        if (next.accessExpiresAt.isAfter(next.refreshExpiresAt)) return false
        if (!next.user.createdAt.isBefore(next.accessExpiresAt)) return false
        if (!next.user.createdAt.isBefore(next.refreshExpiresAt)) return false
        return true
    }

    private fun ready(entry: AccountVaultEntry?): AccountVaultEntry {
        if (entry == null) throw reauth()
        if (entry.serverKey != vault.serverKey || entry.version != 1 ||
            entry.userId != entry.session.user.userId || entry.familyId != entry.session.familyId
        ) {
            throw AccountClientException(AccountClientFailure.VaultUnavailable)
        }
        if (entry.refreshState != AccountRefreshState.Ready) throw reauth()
        return entry
    }

    private fun reauth(): AccountClientException =
        AccountClientException(AccountClientFailure.ReauthenticationRequired)
}
