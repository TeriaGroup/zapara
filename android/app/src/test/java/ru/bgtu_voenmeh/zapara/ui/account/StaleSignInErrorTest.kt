package ru.bgtu_voenmeh.zapara.ui.account

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** #148 (R3-02): ошибка входа не переживает переключение «Вход / Регистрация». */
class StaleSignInErrorTest {
    private val guest = AccountUiState(ready = true, configured = true, guest = true, registrationAvailable = true,
        status = "Неверный логин или пароль.")

    @Test fun switching_to_registration_and_back_clears_the_sign_in_error() {
        val registration = guest.reduce(AccountEvent.ToggleRegistration)
        assertTrue(registration.registration)
        assertEquals("", registration.status)
        val login = registration.copy(status = "Неверный логин или пароль.").reduce(AccountEvent.ToggleRegistration)
        assertEquals(false, login.registration)
        assertEquals("", login.status)
    }

    @Test fun no_registration_means_no_switch_and_the_state_is_untouched() {
        val closed = guest.copy(registrationAvailable = false)
        assertEquals(closed, closed.reduce(AccountEvent.ToggleRegistration))
    }

    @Test fun a_signed_in_account_keeps_its_status() {
        val account = guest.copy(guest = false, status = "Профиль обновлён")
        assertEquals("Профиль обновлён", account.reduce(AccountEvent.ToggleRegistration).status)
    }
}
