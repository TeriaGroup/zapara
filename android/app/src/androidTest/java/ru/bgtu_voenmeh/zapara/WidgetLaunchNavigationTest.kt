package ru.bgtu_voenmeh.zapara

import androidx.activity.ComponentActivity
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performTouchInput
import androidx.compose.ui.test.swipeLeft
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import androidx.test.ext.junit.runners.AndroidJUnit4
import java.time.LocalDate
import java.util.concurrent.atomic.AtomicReference
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleEvent
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleUiState
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchInbox
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchScope
import ru.bgtu_voenmeh.zapara.ui.shell.openSection
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

@RunWith(AndroidJUnit4::class)
class WidgetLaunchNavigationTest {
    @get:Rule val rule = createAndroidComposeRule<ComponentActivity>()

    @Test fun homework_route_keeps_scope_and_generic_header_does_not_restore_the_previous_target() {
        lateinit var nav: NavHostController
        rule.setContent {
            nav = rememberNavController()
            NavHost(nav, startDestination = "start") {
                composable("start") { }
                composable(Section.Homework.pattern, arguments = listOf(
                    navArgument("id") { type = NavType.StringType; nullable = true; defaultValue = null },
                    navArgument("widgetProfile") { type = NavType.StringType; nullable = true; defaultValue = null },
                    navArgument("widgetDatabase") { type = NavType.StringType; nullable = true; defaultValue = null }
                )) { }
            }
        }
        val scope = WidgetLaunchScope("account-a", "profiles/a/zapara.db")
        rule.runOnIdle {
            nav.openSection(Section.Homework, "41", widgetScope = scope, fresh = true)
            assertEquals("41", nav.currentBackStackEntry!!.arguments!!.getString("id"))
            assertEquals("account-a", nav.currentBackStackEntry!!.arguments!!.getString("widgetProfile"))
            assertEquals("profiles/a/zapara.db", nav.currentBackStackEntry!!.arguments!!.getString("widgetDatabase"))
            val first = nav.currentBackStackEntry!!.id
            nav.openSection(Section.Homework, "41", widgetScope = scope, fresh = true)
            assertNotEquals(first, nav.currentBackStackEntry!!.id)
            nav.openSection(Section.Homework, fresh = true)
            assertNull(nav.currentBackStackEntry!!.arguments?.getString("id"))
            assertNull(nav.currentBackStackEntry!!.arguments?.getString("widgetProfile"))
            assertNull(nav.currentBackStackEntry!!.arguments?.getString("widgetDatabase"))
        }
    }

    @Test fun tapping_same_widget_date_after_swiping_schedule_reselects_that_date() {
        val dayA = LocalDate.of(2026, 9, 23)
        val dayB = dayA.plusDays(1)
        val inbox = WidgetLaunchInbox()
        val selected = AtomicReference(dayA)
        lateinit var nav: NavHostController

        rule.setContent {
            nav = rememberNavController()
            ZaparaTheme(ThemeChoice.Light, MotionSettings.Off) {
                CompositionLocalProvider(LocalShellChrome provides ShellChrome(null, false, true) {}) {
                    NavHost(nav, startDestination = Section.Schedule.pattern) {
                        composable(
                            Section.Schedule.pattern,
                            arguments = listOf(navArgument("date") { type = NavType.StringType; nullable = true; defaultValue = null })
                        ) { entry ->
                            val date = entry.arguments?.getString("date")?.let(LocalDate::parse) ?: dayA
                            var state by remember(entry.id) {
                                mutableStateOf(ScheduleUiState(loaded = true, hasGroup = true, today = dayA, selected = date))
                            }
                            selected.set(state.selected)
                            ScheduleSection(state, { event ->
                                if (event is ScheduleEvent.Select) state = state.copy(selected = event.date)
                            }, onOpenMap = {})
                        }
                    }
                }
            }
        }

        val first = inbox.accept("schedule", dayA.toString())!!
        rule.runOnIdle { nav.openSection(first.section, first.argument) }
        rule.waitForIdle()
        assertEquals(dayA, selected.get())

        rule.onNodeWithTag("Schedule.Pager").performTouchInput { swipeLeft() }
        rule.waitUntil(5_000) { selected.get() == dayB }

        inbox.consume(first.id)
        val second = inbox.accept("schedule", dayA.toString())!!
        rule.runOnIdle { nav.openSection(second.section, second.argument) }
        rule.waitUntil(5_000) { selected.get() == dayA }
    }
}
