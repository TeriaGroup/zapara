package ru.bgtu_voenmeh.zapara.ui.account

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AccountUiStateTest {
    @Test
    fun guest_card_is_not_a_nav_section() {
        val guest = AccountUiState(ready = true, configured = true, guest = true, status = "Гостевой профиль: данные доступны без аккаунта и сети.")
        assertTrue(guest.guest)
        assertFalse(guest.busy)
        val account = guest.copy(guest = false, accountName = "Test.User")
        assertFalse(account.guest)
        assertTrue(account.accountName.isNotBlank())
    }
}
