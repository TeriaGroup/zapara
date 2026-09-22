package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.accounts.AccountVaultEntry
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor

/** Candidate profiles open before the vault is switched. Never borrow another profile's session. */
internal fun scopedSyncAccessToken(profile: ProfileDescriptor, entry: AccountVaultEntry?): String? {
    if (profile.isGuest || entry == null || entry.serverKey != profile.serverKey || entry.userId != profile.userId) return null
    return entry.session.accessToken
}
