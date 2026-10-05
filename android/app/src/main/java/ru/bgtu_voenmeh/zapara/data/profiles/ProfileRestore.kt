package ru.bgtu_voenmeh.zapara.data.profiles

import ru.bgtu_voenmeh.zapara.data.accounts.AccountSessionVault

object ProfileRestore {
    suspend fun descriptor(vault: AccountSessionVault): ProfileDescriptor {
        val entry = try {
            vault.acquire().use { it.read() }
        } catch (_: Exception) {
            return ProfileDescriptor.guest()
        } ?: return ProfileDescriptor.guest()
        return try {
            ProfileDescriptor.account(entry.serverKey, entry.session.user.userId)
        } catch (_: Exception) {
            ProfileDescriptor.guest()
        }
    }
}
