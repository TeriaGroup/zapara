package ru.bgtu_voenmeh.zapara.data.profiles

import ru.bgtu_voenmeh.zapara.data.accounts.AccountSession
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSessionVault
import ru.bgtu_voenmeh.zapara.data.accounts.AccountVaultEntry

data class ProfileSwitchResult(val committed: Boolean, val current: ProfileGraph)

class ProfileCoordinator(
    initial: ProfileGraph,
    private val open: (ProfileDescriptor) -> ProfileGraph,
    private val vault: AccountSessionVault,
    private val idleTimeoutMs: Long = 10_000,
    private val onPublished: (ProfileGraph) -> Unit = {}
) {
    var current: ProfileGraph = initial
        private set
    var generation: Long = 0
        private set

    suspend fun commitSession(session: AccountSession, serverKey: String): ProfileSwitchResult {
        val target = ProfileDescriptor.account(serverKey, session.user.userId)
        return switch(target) {
            vault.acquire().use { lease ->
                lease.write(AccountVaultEntry.ready(serverKey, session))
            }
        }
    }

    suspend fun logout(remoteLogout: (suspend (AccountSession) -> Unit)? = null): ProfileSwitchResult {
        var session: AccountSession? = null
        val result = switch(ProfileDescriptor.guest()) {
            vault.acquire().use { lease ->
                session = lease.read()?.session
                lease.clear()
            }
        }
        val posted = session
        if (result.committed && posted != null && remoteLogout != null) {
            try {
                remoteLogout(posted)
            } catch (_: Exception) {
            }
        }
        return result
    }

    private suspend fun switch(target: ProfileDescriptor, persist: suspend () -> Unit): ProfileSwitchResult {
        val old = current
        if (old.descriptor == target) {
            persist()
            return ProfileSwitchResult(true, old)
        }
        val candidate = try {
            open(target)
        } catch (_: Exception) {
            return ProfileSwitchResult(false, old)
        }
        old.work.stopAccepting()
        val idle = old.work.whenIdle(idleTimeoutMs)
        if (!idle) {
            old.work.resume()
            candidate.close()
            return ProfileSwitchResult(false, old)
        }
        try {
            persist()
        } catch (e: Exception) {
            old.work.resume()
            candidate.close()
            throw e
        }
        old.close()
        current = candidate
        generation++
        onPublished(candidate)
        return ProfileSwitchResult(true, candidate)
    }

    fun close() {
        current.close()
    }
}
