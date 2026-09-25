package ru.bgtu_voenmeh.zapara.data.accounts

import java.time.Duration
import java.time.Instant
import java.util.UUID

/**
 * A persisted attempt ID makes v2 rotation safe to resume after a lost response or process restart.
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
            var current = validated(lease.read())
            if (current.refreshState == AccountRefreshState.Ready &&
                current.session.accessExpiresAt.isAfter(clock().plusSeconds(30))) return current.session
            repeat(2) {
                val next = rotate(lease, current)
                if (next.accessExpiresAt.isAfter(clock().plusSeconds(30))) return next
                current = AccountVaultEntry.ready(vault.serverKey, next)
            }
            throw AccountClientException(AccountClientFailure.ServerUnavailable)
        }
    }

    private suspend fun rotate(lease: AccountVaultLease, current: AccountVaultEntry): AccountSession {
        if (!current.session.refreshExpiresAt.isAfter(clock())) throw reauth()
        val attemptId = when (current.refreshState) {
            AccountRefreshState.Ready -> UUID.randomUUID().toString().also {
                lease.write(current.copy(refreshState = AccountRefreshState.Pending, refreshAttemptId = it))
            }
            AccountRefreshState.Pending -> current.refreshAttemptId ?: throw reauth()
        }
        val next = try {
            client.refreshResumable(current.session.refreshToken, attemptId)
        } catch (error: AccountClientException) {
            when (error.failure) {
                AccountClientFailure.NotConfigured -> {
                    // The old route has no retry key. Persist this before sending its one-shot request.
                    lease.write(current.copy(refreshState = AccountRefreshState.Pending, refreshAttemptId = null))
                    client.refresh(current.session.refreshToken)
                }
                AccountClientFailure.InvalidSession, AccountClientFailure.SessionNotFound -> {
                    lease.write(current.copy(refreshState = AccountRefreshState.Pending, refreshAttemptId = null))
                    throw error
                }
                else -> throw error
            }
        }
        if (!acceptable(current.session, next)) {
            throw AccountClientException(AccountClientFailure.InvalidPayload)
        }
        lease.write(AccountVaultEntry.ready(vault.serverKey, next))
        return next
    }

    private fun acceptable(old: AccountSession, next: AccountSession): Boolean {
        if (next.user.userId != old.user.userId || next.familyId != old.familyId) return false
        if (next.user.createdAt != old.user.createdAt) return false
        if (next.refreshExpiresAt.isAfter(old.refreshExpiresAt) ||
            Duration.between(next.refreshExpiresAt, old.refreshExpiresAt) > Duration.ofNanos(1_000)) return false
        if (next.accessToken == old.accessToken || next.refreshToken == old.refreshToken) return false
        if (next.accessExpiresAt.isAfter(next.refreshExpiresAt)) return false
        if (!next.user.createdAt.isBefore(next.accessExpiresAt)) return false
        if (!next.user.createdAt.isBefore(next.refreshExpiresAt)) return false
        return true
    }

    private fun validated(entry: AccountVaultEntry?): AccountVaultEntry {
        if (entry == null) throw reauth()
        if (entry.serverKey != vault.serverKey || entry.version != 1 ||
            entry.userId != entry.session.user.userId || entry.familyId != entry.session.familyId
        ) {
            throw AccountClientException(AccountClientFailure.VaultUnavailable)
        }
        return entry
    }

    private fun reauth(): AccountClientException =
        AccountClientException(AccountClientFailure.ReauthenticationRequired)
}
