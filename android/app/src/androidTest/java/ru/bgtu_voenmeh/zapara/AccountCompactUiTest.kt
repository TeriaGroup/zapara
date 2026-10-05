package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.UiDevice
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.account.AccountCard
import ru.bgtu_voenmeh.zapara.ui.account.AccountEvent
import ru.bgtu_voenmeh.zapara.ui.account.AccountUiState
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import ru.bgtu_voenmeh.zapara.ui.chat.LocalAvatarStore
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.avatars.AvatarHttpClient
import ru.bgtu_voenmeh.zapara.data.avatars.AvatarStore

class AccountCompactUiTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null
    private var originalFontScale: String? = null

    @Test fun photo_actions_open_from_avatar_without_mutating_profile_data() {
        val calls = mutableListOf<String>()
        val transport = HttpExchange { call -> calls += call.method; HttpReply(404, byteArrayOf()) }
        val avatars = AvatarStore("11111111-1111-4111-8111-111111111111",
            AvatarHttpClient(transport, AccountServerScope.parse("https://example.test")), { "za_" + "A".repeat(43) })
        try {
            val current = OwnedTestHost.launch().also { host = it }
            current.scenario.onActivity { activity ->
                activity.setContent {
                    ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                        CompositionLocalProvider(LocalAvatarStore provides avatars) {
                            Column(Modifier.fillMaxSize().background(Zapara.colors.canvas)
                                .verticalScroll(rememberScrollState()).padding(Zapara.space.l)) {
                                AccountCard(AccountUiState(configured = true, ready = true, guest = false,
                                    accountName = "Иван Петров", displayName = "Иван Петров", profileNameBaseline = "Иван Петров"), {})
                            }
                        }
                    }
                }
            }
            rule.waitForIdle()
            rule.onNodeWithTag("Avatar.Choose").assertDoesNotExist()
            rule.onNodeWithTag("Avatar.Remove").assertDoesNotExist()
            rule.onNodeWithTag("Avatar.OpenMenu").assertIsDisplayed().performClick()
            rule.onNodeWithTag("Avatar.Choose").assertIsDisplayed()
            rule.onNodeWithTag("Avatar.Remove").assertIsDisplayed()
            Frames.capture(current.activity, "account-compact-photo-menu")
            rule.onNodeWithTag("Avatar.Menu.Close").performClick()
            rule.onNodeWithTag("Avatar.Choose").assertDoesNotExist()
            rule.runOnIdle { assertTrue(calls.all { it == "GET" }) }
        } finally { avatars.close() }
    }

    @After fun closeHost() {
        try {
            host?.close()
        } finally {
            host = null
            originalFontScale?.let(FontScaleReadiness::setAndAwait)
            originalFontScale = null
        }
    }

    @Test fun signed_account_starts_compact_and_keeps_edit_documents_and_logout_reachable() {
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        originalFontScale = device.executeShellCommand("settings get system font_scale").trim().also {
            require(it.toFloatOrNull() != null)
        }
        FontScaleReadiness.setAndAwait("1.0")
        val current = OwnedTestHost.launch().also { host = it }
        FontScaleReadiness.awaitHost(current, 1f)

        val initialName = "Иван Петров"
        val updatedName = "Иван Сидоров"
        val events = mutableListOf<AccountEvent>()
        var renderedState: AccountUiState? = null
        var composedScale = Float.NaN
        val status = InstrumentationRegistry.getInstrumentation().targetContext.getString(R.string.account_local)
        current.scenario.onActivity { activity ->
            activity.setContent {
                val actualScale = LocalDensity.current.fontScale
                SideEffect { composedScale = actualScale }
                ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                    var state by remember {
                        mutableStateOf(AccountUiState(
                            configured = true, ready = true, guest = false,
                            accountName = initialName, displayName = initialName,
                            profileNameBaseline = initialName, hasPassword = false,
                            status = status
                        ))
                    }
                    SideEffect { renderedState = state }
                    Column(Modifier.fillMaxSize().background(Zapara.colors.canvas)
                        .verticalScroll(rememberScrollState()).padding(Zapara.space.l)) {
                        AccountCard(state, onEvent = { event ->
                            events += event
                            state = state.reduce(event)
                        })
                    }
                }
            }
        }
        rule.waitForIdle()
        rule.runOnIdle { assertEquals(1f, composedScale) }

        rule.onNodeWithTag("Account.Name").assertIsDisplayed()
        rule.onNodeWithText(initialName).assertIsDisplayed()
        rule.onNodeWithTag("Account.ProfileName").assertDoesNotExist()
        rule.onNodeWithTag("Legal.Agreement").assertDoesNotExist()
        rule.onNodeWithTag("Account.Logout").assertDoesNotExist()
        assertTrue(Frames.capture(current.activity, "account-compact-summary").length() > 1000)

        rule.onNodeWithTag("Account.ProfileEdit").assertIsDisplayed().performClick()
        rule.onNodeWithTag("Account.ProfileName").assertIsDisplayed()
        assertTrue(Frames.capture(current.activity, "account-compact-edit-name").length() > 1000)
        rule.onNodeWithTag("Account.ProfileName").performTextReplacement(updatedName)
        rule.onNodeWithTag("Account.ProfileSave").assertIsEnabled()
        rule.runOnIdle {
            assertEquals(updatedName, renderedState?.displayName)
            assertTrue(events.contains(AccountEvent.ProfileName(updatedName)))
        }
        rule.onNodeWithTag("Account.ProfileCancel").performClick()
        rule.onNodeWithTag("Account.ProfileName").assertDoesNotExist()
        rule.runOnIdle {
            assertEquals(initialName, renderedState?.displayName)
            assertTrue(events.contains(AccountEvent.CancelProfile))
        }

        rule.onNodeWithTag("Account.Documents").performScrollTo().performClick()
        rule.onNodeWithTag("Legal.Agreement").performScrollTo().assertIsDisplayed()
        rule.onNodeWithTag("Legal.Policy").performScrollTo().assertIsDisplayed()
        assertTrue(Frames.capture(current.activity, "account-compact-documents").length() > 1000)
        rule.onNodeWithTag("Account.Documents").performScrollTo().performClick()
        rule.onNodeWithTag("Legal.Agreement").assertDoesNotExist()
        rule.onNodeWithTag("Legal.Policy").assertDoesNotExist()

        rule.onNodeWithTag("Account.Security").performScrollTo().performClick()
        rule.onNodeWithTag("Account.Logout").performScrollTo().assertIsDisplayed().performClick()
        rule.onNodeWithTag("Account.ConfirmLogout").performScrollTo().assertIsDisplayed()
        rule.runOnIdle {
            assertTrue(events.contains(AccountEvent.RequestLogout))
            assertEquals(true, renderedState?.confirmLogout)
        }
        rule.onNodeWithTag("Account.CancelLogout").performScrollTo().performClick()
        rule.onNodeWithTag("Account.ConfirmLogout").assertDoesNotExist()
        rule.onNodeWithTag("Account.Logout").performScrollTo().assertIsDisplayed()
        rule.runOnIdle {
            assertTrue(events.contains(AccountEvent.CancelLogout))
            assertFalse(renderedState?.confirmLogout ?: true)
            assertFalse(events.contains(AccountEvent.ConfirmLogout))
        }
    }
}
