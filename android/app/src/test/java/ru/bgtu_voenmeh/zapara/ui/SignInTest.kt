package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import ru.bgtu_voenmeh.zapara.ui.account.confirmMismatchShown
import ru.bgtu_voenmeh.zapara.ui.account.passwordErrorShown
import ru.bgtu_voenmeh.zapara.ui.account.AccountFormMemory
import ru.bgtu_voenmeh.zapara.ui.settings.LegalReturn
import org.junit.Test
import java.io.File

/** #109: одно состояние «не вошли» (AN-14) и форма входа (AN-16). */
class SignInTest {
    private fun src(path: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/$path").readText()
    private val account = src("account/AccountUi.kt")

    @Test fun chats_communities_and_group_share_one_signed_out_state_with_sign_in() {
        val shared = src("components/SignedOutState.kt")
        assertTrue(shared.contains("stringResource(R.string.signed_out_title)") && shared.contains("stringResource(R.string.account_login)"))
        assertTrue(src("inbox/InboxSection.kt").contains("SignedOutState(R.drawable.ic_chat,"))
        assertTrue(src("communities/CommunitiesSection.kt").contains("components.SignedOutState("))
        assertTrue(src("groups/GroupSection.kt").contains("SignedOutState(R.drawable.ic_users,"))
        assertEquals("Войти", XmlCopy.get("account_login"))
        assertTrue(XmlCopy.get("signed_out_chats_hint").contains("без аккаунта"))
        assertTrue(XmlCopy.get("signed_out_chats_hint").startsWith("Чат группы и личные чаты — после входа."))
        assertTrue(XmlCopy.get("signed_out_communities_hint").contains("без аккаунта"))
    }

    @Test fun login_has_tabs_instead_of_a_mode_button() {
        assertTrue(account.contains("listOf(stringResource(R.string.account_tab_login), stringResource(R.string.account_tab_register))"))
        assertFalse(account.contains("stringResource(R.string.account_mode)"))
        assertEquals("Вход", XmlCopy.get("account_tab_login"))
        assertEquals("Регистрация", XmlCopy.get("account_tab_register"))
    }

    @Test fun one_primary_that_is_enabled_and_reveals_errors() {
        assertTrue(account.contains("val submit = { if (ready) onEvent(AccountEvent.Submit) else memory.attempted = true }"))
        assertFalse(account.contains("enabled = state.canSubmitCredentials"))
        assertEquals(2, Regex("submit,\\n\\s+enabled = !state.busy && !state.externalPending, busy = state.busy").findAll(account).count())
    }

    @Test fun errors_show_after_blur_or_submit() {
        assertTrue(account.contains("fun shown(field: String) = attempted || field in touched"))
        assertTrue(account.contains(".onFocusChanged { if (it.isFocused) hadFocus = true else if (hadFocus) onBlur?.invoke() }"))
        assertTrue(account.contains("if (!state.usernameValid && shown(\"username\"))"))
        assertTrue(account.contains("if (attempted && !state.documentsAccepted)"))
    }

    @Test fun consent_row_is_48dp_with_inline_links_and_forgot_password_is_a_link() {
        val accept = account.substringAfter("private fun AcceptDocuments(").substringBefore("\n@Composable")
        assertTrue(accept.contains("heightIn(min = 48.dp)"))
        assertTrue(accept.contains("tag = \"Account.AcceptAgreement\"") && accept.contains("tag = \"Account.AcceptPolicy\""))
        // Ссылки, а не кнопки: подчёркнутый текст, цель 48 dp.
        assertTrue(accept.contains("DocLink(stringResource(R.string.face_agreement), tag = \"Account.AcceptAgreement\")"))
        assertFalse(accept.contains("ZButton(stringResource(R.string.face_agreement)"))
        val link = account.substring(account.indexOf("private fun DocLink("), account.indexOf("private fun DocLink(") + 600)
        assertTrue(link.contains("TextDecoration.Underline") && link.contains("heightIn(min = 48.dp)"))
        assertTrue(account.contains("ghost = true, quiet = true, tag = \"Account.RecoveryToggle\""))
    }

    @Test fun confirm_mismatch_shows_after_a_submit_attempt_even_when_empty() {
        val f = ::confirmMismatchShown
        assertTrue("после попытки и с пустым подтверждением", f(true, "", "secret-password"))
        assertTrue("введено и не совпадает", f(false, "secret", "secret-password"))
        assertFalse("ничего не введено и попытки не было", f(false, "", "secret-password"))
        assertFalse("совпадает", f(true, "secret-password", "secret-password"))
        assertFalse("оба пустые после попытки — ошибки длины, не несовпадения", f(true, "", ""))
        val ui = java.io.File("src/main/java/ru/bgtu_voenmeh/zapara/ui/account/AccountUi.kt").readText()
        assertTrue(ui.contains("if (confirmMismatchShown(attempted, confirmPassword, state.password))"))
        assertFalse(ui.contains("if (confirmPassword.isNotEmpty() && confirmPassword != state.password)"))
    }

    private val accountUi get() = java.io.File("src/main/java/ru/bgtu_voenmeh/zapara/ui/account/AccountUi.kt").readText()
    private val settings get() = java.io.File("src/main/java/ru/bgtu_voenmeh/zapara/ui/settings/SettingsSection.kt").readText()

    @Test fun empty_registration_password_shows_an_error_after_submit() {
        assertTrue("пустой пароль после попытки", passwordErrorShown(shown = true, passwordValid = false))
        assertFalse("до попытки и ухода с поля — молчим", passwordErrorShown(shown = false, passwordValid = false))
        assertFalse(passwordErrorShown(shown = true, passwordValid = true))
        // ошибка больше не спрятана за else у PasswordProgress — показывается и в регистрации
        assertTrue(accountUi.contains("if (state.registration) PasswordProgress(state.password)\n"))
        assertTrue(accountUi.contains("if (passwordErrorShown(shown(\"password\"), state.passwordValid)) Text(stringResource(R.string.ux60_account_password_hint)"))
        assertFalse(accountUi.contains("else if (!state.passwordValid && shown(\"password\"))"))
    }

    @Test fun opening_a_consent_document_keeps_the_registration_input() {
        val memory = AccountFormMemory()
        memory.mode(registration = true)
        memory.confirmPassword = "secret-password"; memory.attempted = true; memory.touched += "name"
        // возврат с документа: тот же режим — ничего не сбрасывается
        memory.mode(registration = true)
        assertEquals("secret-password", memory.confirmPassword)
        assertTrue(memory.attempted); assertEquals(listOf("name"), memory.touched.toList())
        // смена режима начинает форму заново
        memory.mode(registration = false)
        assertEquals("", memory.confirmPassword); assertFalse(memory.attempted); assertTrue(memory.touched.isEmpty())
        // пароль из состояния не чистится, если документ открыт из регистрации; в остальных случаях — как раньше
        assertTrue(LegalReturn.keepsRegistration(guest = true, registration = true))
        assertFalse(LegalReturn.keepsRegistration(guest = true, registration = false))
        assertFalse(LegalReturn.keepsRegistration(guest = false, registration = false))
        // memory держит экран настроек — над ранним return к документу, — а не сама карточка
        val held = settings.indexOf("val accountForm = remember { ru.bgtu_voenmeh.zapara.ui.account.AccountFormMemory() }")
        val docReturn = settings.indexOf("LegalDocumentPage(legalId!!")
        assertTrue(held in 0 until docReturn)
        assertTrue(settings.contains("if (!LegalReturn.keepsRegistration(account.guest, account.registration)) onAccount(AccountEvent.ClearSensitive)"))
        assertTrue(settings.contains("}, memory = accountForm) }"))
        assertFalse("подтверждение не в remember карточки", accountUi.contains("var confirmPassword by remember(state.registration)"))
        assertFalse(accountUi.contains("var attempted by remember(state.registration)"))
    }

    @Test fun account_isolation_notice_stays_visible_during_registration() {
        val block = accountUi.substring(accountUi.indexOf("if (!state.showAccount) {"), accountUi.indexOf("if (!state.configured || !state.ready) return@ZCard"))
        val notice = block.indexOf("R.string.account_isolation")
        val guard = block.indexOf("if (!(state.guest && state.registration))")
        assertTrue("предупреждение — до условия регистрации", notice in 0 until guard)
        assertTrue("кнопки документов — под условием", block.indexOf("tag = \"Legal.Agreement\"") > guard)
        assertFalse(accountUi.contains("if (!state.showAccount && !(state.guest && state.registration))"))
    }
}
