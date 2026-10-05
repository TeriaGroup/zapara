package ru.bgtu_voenmeh.zapara.data.sync

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.*
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import java.util.UUID

class PrivateSyncSessionScopeTest {
    @Test fun candidate_profile_cannot_read_previous_accounts_token_before_vault_switch() {
        val server = "A".repeat(64)
        val user = UUID.randomUUID().toString()
        val session = AccountSession(AccountUser(user, "Test", null, NOW), UUID.randomUUID().toString(),
            ACCESS, "unused refresh", "Bearer", NOW.plusSeconds(600), NOW.plusSeconds(3600))
        val entry = AccountVaultEntry.ready(server, session)
        assertEquals(ACCESS, scopedSyncAccessToken(ProfileDescriptor.account(server, user), entry))
        assertNull(scopedSyncAccessToken(ProfileDescriptor.account(server, UUID.randomUUID().toString()), entry))
        assertNull(scopedSyncAccessToken(ProfileDescriptor.account("B".repeat(64), user), entry))
        assertNull(scopedSyncAccessToken(ProfileDescriptor.guest(), entry))
        assertNull(scopedSyncAccessToken(ProfileDescriptor.account(server, user), null))
    }
}
