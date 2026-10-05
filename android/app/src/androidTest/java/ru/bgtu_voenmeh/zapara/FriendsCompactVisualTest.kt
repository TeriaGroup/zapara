package ru.bgtu_voenmeh.zapara

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import java.time.LocalDate
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.friends.FriendEncounter
import ru.bgtu_voenmeh.zapara.ui.friends.FriendUi
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsEvent
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsSection
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class FriendsCompactVisualTest {
    @get:Rule val rule = createEmptyComposeRule()
    private var host: OwnedTestHost? = null

    @After fun closeHost() { host?.close() }

    @Test fun light_100_friends_controls() = exercise(ThemeChoice.Light, 1f, "fullqa-friends-light-100")
    @Test fun dark_200_friends_controls() = exercise(ThemeChoice.Dark, 2f, "fullqa-friends-dark-200")

    private fun exercise(theme: ThemeChoice, scale: Float, prefix: String) {
        val today = LocalDate.now()
        var state by mutableStateOf(FriendsUiState(
            loaded = true,
            friends = listOf(
                FriendUi(1, 0, "A863C", "Ирина", true, 0),
                FriendUi(2, 1, "B864C", "Павел", false, 1)
            ),
            myGroupId = "M100",
            profileName = "Тестовый профиль",
            hasOwnSchedule = true,
            encounters = listOf(
                FriendEncounter(today, "09:00", "Встреча сегодня", "C111", "", "326", "#FF9900", 75),
                FriendEncounter(today.plusDays(1), "10:45", "Встреча завтра", "D222", "", "410", "#00AA55", 50),
                FriendEncounter(today.plusDays(2), "13:20", "Встреча позже", "E333", "", "507", "#3355CC", 25)
            )
        ))
        val events = mutableListOf<FriendsEvent>()
        val opened = mutableListOf<Triple<FriendEncounter, String, String>>()
        val current = OwnedTestHost.launch().also { host = it }
        current.scenario.onActivity { activity -> activity.setContent {
            val density = LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density, scale)) {
                ZaparaTheme(theme, MotionSettings.Off) {
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                        Surface(Modifier.fillMaxSize(), color = Zapara.colors.canvas) {
                            FriendsSection(state, onEvent = { event ->
                                events += event
                                if (event == FriendsEvent.RefreshSchedules) state = state.copy(refreshing = true)
                            }, onOpenEncounter = { encounter, group, profile ->
                                opened += Triple(encounter, group, profile)
                            })
                        }
                    }
                }
            }
        } }
        rule.waitForIdle()
        current.awaitForeground()

        val search = rule.onNodeWithTag("Friends.Search").assertIsDisplayed().fetchSemanticsNode().boundsInRoot
        val filter = rule.onNodeWithTag("Friends.Filter").assertIsDisplayed().fetchSemanticsNode().boundsInRoot
        assertTrue("Search/filter spacing", filter.top - search.bottom >= current.activity.resources.displayMetrics.density * 7f)
        rule.onNodeWithTag("Friends.Filter.0").assertIsSelected()
        if (scale >= 1.5f) assertTabsFit("Friends.Filter", listOf("Все", "Включены", "Выключены"))
        Frames.capture(current.activity, "$prefix-loaded")

        rule.onNodeWithTag("Friends.Search").performTextInput("B864C")
        rule.onNodeWithTag("Friends.Search").performImeAction()
        rule.onNodeWithText(current.activity.getString(R.string.ux100_common_no_friends_found)).assertDoesNotExist()
        Frames.capture(current.activity, "$prefix-search")
        rule.onNodeWithTag("Friends.Filter.1").performClick().assertIsSelected()
        rule.onNodeWithText(current.activity.getString(R.string.ux100_common_no_friends_found)).assertIsDisplayed()
        rule.onNodeWithTag("Friends.Filter.2").performClick().assertIsSelected()
        rule.onNodeWithText(current.activity.getString(R.string.ux100_common_no_friends_found)).assertDoesNotExist()
        Frames.capture(current.activity, "$prefix-disabled")
        rule.onNodeWithTag("Friends.Reset").performClick()
        rule.onNodeWithTag("Friends.Filter.0").assertIsSelected()

        rule.onNodeWithTag("Friends.Day.0").performScrollTo().assertIsSelected()
        if (scale >= 1.5f) assertTabsFit("Friends.Day", listOf("Все дни", "Сегодня", "Завтра"))
        rule.onNodeWithText("Встреча сегодня").assertExists()
        rule.onNodeWithText("Встреча завтра").assertExists()
        rule.onNodeWithText("Встреча позже").assertExists()
        rule.onNodeWithTag("Friends.OpenEncounter.0").performScrollTo().performClick()
        rule.runOnIdle { assertEquals(Triple(state.encounters[0], "M100", "Тестовый профиль"), opened.single()) }
        rule.onNodeWithTag("Friends.Day.1").performScrollTo().performClick().assertIsSelected()
        rule.onNodeWithText("Встреча сегодня").assertExists()
        rule.onNodeWithText("Встреча завтра").assertDoesNotExist()
        rule.onNodeWithTag("Friends.Day.2").performScrollTo().performClick().assertIsSelected()
        rule.onNodeWithText("Встреча завтра").assertExists()
        rule.onNodeWithText("Встреча сегодня").assertDoesNotExist()
        Frames.capture(current.activity, "$prefix-tomorrow")
        rule.onNodeWithTag("Friends.Day.0").performScrollTo().performClick().assertIsSelected()

        rule.onNodeWithTag("Friends.RefreshSchedules").performScrollTo().performClick()
        rule.runOnIdle { assertEquals(FriendsEvent.RefreshSchedules, events.last()) }
        rule.onNodeWithTag("Friends.RefreshSchedules").assertIsNotEnabled()
        Frames.capture(current.activity, "$prefix-refreshing")

        rule.onNodeWithTag("Friends.PickForecastDate").performScrollTo().performClick()
        val device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
        assertTrue("Native date picker opened", device.wait(Until.hasObject(By.clazz("android.widget.DatePicker")), 3000))
        Frames.capture(current.activity, "$prefix-date-picker")
        device.pressBack()
        rule.waitForIdle()
    }

    private fun assertTabsFit(tag: String, labels: List<String>) {
        labels.forEachIndexed { index, label ->
            val tab = rule.onNodeWithTag("$tag.$index").performScrollTo().assertIsDisplayed()
                .assertWidthIsAtLeast(48.dp).assertHeightIsAtLeast(48.dp)
            val parent = rule.onNodeWithTag(tag).fetchSemanticsNode().boundsInRoot
            val bounds = tab.fetchSemanticsNode().boundsInRoot
            assertTrue("$tag.$index escapes horizontally: tab=$bounds parent=$parent",
                bounds.left >= parent.left - 1f && bounds.right <= parent.right + 1f)
            rule.onNode(hasText(label) and hasAnyAncestor(hasTestTag("$tag.$index")),
                useUnmergedTree = true).assertIsDisplayed()
        }
    }
}
