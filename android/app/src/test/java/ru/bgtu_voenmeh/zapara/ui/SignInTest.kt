package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
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
        assertTrue(account.contains("val submit = { if (ready) onEvent(AccountEvent.Submit) else attempted = true }"))
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
}
