package ru.bgtu_voenmeh.zapara.ui.account

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * #148 (R3-02): ошибка входа не переживает переключение «Вход / Регистрация», а обычная строка гостя
 * (пояснение гостевого профиля, «Пароль изменён» после восстановления) при переключении остаётся.
 */
class StaleSignInErrorTest {
    private val guestText = "Гостевой профиль: данные доступны без аккаунта и сети."
    private val bad = "Неверный логин или пароль."
    private val guest = AccountUiState(ready = true, configured = true, guest = true, registrationAvailable = true,
        guestStatus = guestText, status = guestText)
    private val rejected = guest.copy(status = bad, formFailure = bad)

    @Test fun switching_to_registration_and_back_clears_the_sign_in_error() {
        val registration = rejected.reduce(AccountEvent.ToggleRegistration)
        assertTrue(registration.registration)
        assertEquals(guestText, registration.status)
        assertNull(registration.formFailure)
        val login = registration.copy(status = bad, formFailure = bad).reduce(AccountEvent.ToggleRegistration)
        assertEquals(false, login.registration)
        assertEquals(guestText, login.status)
    }

    @Test fun a_fresh_guest_keeps_the_guest_explanation() {
        assertEquals(guestText, guest.reduce(AccountEvent.ToggleRegistration).status)
        assertEquals(guestText, guest.reduce(AccountEvent.ToggleRegistration).reduce(AccountEvent.ToggleRegistration).status)
    }

    @Test fun recovery_success_survives_opening_registration() {
        val recovered = guest.copy(status = "Пароль изменён. Войдите с новым паролем.")
        assertEquals(recovered.status, recovered.reduce(AccountEvent.ToggleRegistration).status)
        // Старая пометка сбоя к новому тексту не относится — он тоже остаётся.
        assertEquals(recovered.status, recovered.copy(formFailure = bad).reduce(AccountEvent.ToggleRegistration).status)
    }

    @Test fun no_registration_means_no_switch_and_the_state_is_untouched() {
        val closed = rejected.copy(registrationAvailable = false)
        assertEquals(closed, closed.reduce(AccountEvent.ToggleRegistration))
    }

    @Test fun a_signed_in_account_keeps_its_status() {
        val account = guest.copy(guest = false, status = "Профиль обновлён")
        assertEquals("Профиль обновлён", account.reduce(AccountEvent.ToggleRegistration).status)
    }

    @Test fun view_model_marks_sign_in_and_registration_failures_as_form_failures() {
        val vm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/account/AccountViewModel.kt").readText()
        assertTrue(vm.contains("launchOp(allowIdentityTransition = true, form = true)"))
        assertTrue(vm.contains("launchOp(provider, form = login)"))
        assertTrue(vm.contains("guestStatus = runtime.strings(R.string.account_guest)"))
        for (call in listOf("fail(failureText(e.failure), form)", "fail(runtime.strings(R.string.account_validation), form)",
            "fail(runtime.strings(R.string.account_failed), form)", "formFail(runtime.strings(R.string.account_accept_required))",
            "formFail(runtime.strings(R.string.account_registration_unavailable))"))
            assertTrue(call, vm.contains(call))
    }
}
