package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.remember
import kotlinx.coroutines.flow.MutableStateFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.ZaparaApplication
import ru.bgtu_voenmeh.zapara.ui.account.AccountViewModel
import ru.bgtu_voenmeh.zapara.ui.components.ToastHost
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsSection
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsViewModel
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkSection
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkViewModel
import ru.bgtu_voenmeh.zapara.ui.maps.MapsSection
import ru.bgtu_voenmeh.zapara.ui.maps.MapsViewModel
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleViewModel
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsSection
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsViewModel
import ru.bgtu_voenmeh.zapara.ui.summary.SummarySection
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryViewModel
import ru.bgtu_voenmeh.zapara.ui.teachers.TeachersSection
import ru.bgtu_voenmeh.zapara.ui.teachers.TeachersViewModel
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ProvideSectionEntry
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeCrossfade
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import ru.bgtu_voenmeh.zapara.ui.week.WeekSection
import ru.bgtu_voenmeh.zapara.ui.week.WeekViewModel

@OptIn(ExperimentalComposeUiApi::class)
@Composable
fun ZaparaApp(container: AppContainer) {
    val activity = LocalContext.current as ComponentActivity
    val app = activity.application as? ZaparaApplication
    val genFlow = app?.host?.generation ?: remember { MutableStateFlow(0L) }
    val generation by genFlow.collectAsStateWithLifecycle()
    key(generation) {
        val live = app?.container ?: container
        val owner = app?.host ?: activity
        ZaparaAppBody(live, activity, owner, app?.host)
    }
}

@OptIn(ExperimentalComposeUiApi::class)
@Composable
private fun ZaparaAppBody(
    container: AppContainer,
    activity: ComponentActivity,
    owner: androidx.lifecycle.ViewModelStoreOwner,
    host: ru.bgtu_voenmeh.zapara.AndroidProfileHost?
) {
    val shellVm: ShellViewModel = viewModel(owner, factory = ShellViewModel.factory(container))
    val state by shellVm.state.collectAsStateWithLifecycle()
    val update by container.update.state.collectAsStateWithLifecycle()
    ZaparaTheme(choice = state.theme, motion = MotionSettings(state.animations, 1f)) {
        val motion = Zapara.motion
        val slidePx = with(LocalDensity.current) { 8.dp.roundToPx() }
        ThemeCrossfade(key = Zapara.colors.isDark, motion = motion) {
        val nav = rememberNavController()
        val entry by nav.currentBackStackEntryAsState()
        val current = Section.byRoute(entry?.destination?.route) ?: Section.Schedule
        val chip = state.groupName?.let { ShellLogic.chip(it, state.odd, container.copy) }
        val chrome = ShellChrome(chip, state.stale, state.hasGroup) {
            shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.GroupPicker))
        }
        val themeDesc = if (Zapara.colors.isDark) "dark" else "light"
        BackHandler(enabled = state.overlay != ShellOverlay.None || current != Section.Schedule) {
            when {
                state.overlay != ShellOverlay.None ->
                    shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None))
                else -> nav.openSection(Section.Schedule)
            }
        }
        CompositionLocalProvider(LocalShellChrome provides chrome) {
            Box(Modifier.fillMaxSize()) {
                Scaffold(
                    modifier = Modifier.semantics {
                        testTagsAsResourceId = true
                        stateDescription = themeDesc
                    },
                    containerColor = Zapara.colors.canvas,
                    bottomBar = {
                        ZBottomBar(
                            current = current,
                            sectionsActive = current !in Section.bar || state.overlay == ShellOverlay.Sections,
                            homeworkBadge = state.homeworkBadge,
                            updateBadge = update.hasUpdate,
                            onSection = { nav.openSection(it) },
                            onSections = { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.Sections)) }
                        )
                    }
                ) { padding ->
                    Box(Modifier.padding(padding).fillMaxSize()) {
                        NavHost(
                            navController = nav,
                            startDestination = Section.Schedule.pattern,
                            enterTransition = { fadeIn(tween(motion.ms(Durations.section), easing = ZaparaEase)) + slideInHorizontally(tween(motion.ms(Durations.section), easing = ZaparaEase)) { slidePx } },
                            exitTransition = { fadeOut(tween(motion.ms(Durations.section), easing = ZaparaEase)) },
                            popEnterTransition = { fadeIn(tween(motion.ms(Durations.section), easing = ZaparaEase)) + slideInHorizontally(tween(motion.ms(Durations.section), easing = ZaparaEase)) { slidePx } },
                            popExitTransition = { fadeOut(tween(motion.ms(Durations.section), easing = ZaparaEase)) }
                        ) {
                            composable(
                                Section.Schedule.pattern,
                                arguments = listOf(navArgument("date") { type = NavType.StringType; nullable = true; defaultValue = null })
                            ) { dest ->
                                ProvideSectionEntry {
                                val date = dest.arguments?.getString("date")
                                val vm: ScheduleViewModel = viewModel(factory = ScheduleViewModel.factory(container, date))
                                val s by vm.state.collectAsStateWithLifecycle()
                                LaunchedEffect(date) {
                                    date?.let { runCatching { java.time.LocalDate.parse(it) }.getOrNull() }?.let {
                                        vm.onEvent(ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleEvent.Select(it))
                                    }
                                }
                                ScheduleSection(s, vm::onEvent) { room -> nav.openSection(Section.Maps, room) }
                                }
                            }
                            composable(
                                Section.Maps.pattern,
                                arguments = listOf(navArgument("room") { type = NavType.StringType; nullable = true; defaultValue = null })
                            ) { dest ->
                                ProvideSectionEntry {
                                val room = dest.arguments?.getString("room")
                                val vm: MapsViewModel = viewModel(factory = MapsViewModel.factory(container, room))
                                val s by vm.state.collectAsStateWithLifecycle()
                                LaunchedEffect(room) { if (!room.isNullOrBlank()) vm.onEvent(ru.bgtu_voenmeh.zapara.ui.maps.MapsEvent.ShowRoom(room)) }
                                MapsSection(s, vm::onEvent)
                                }
                            }
                            composable(Section.Homework.route) {
                                ProvideSectionEntry {
                                val vm: HomeworkViewModel = viewModel(factory = HomeworkViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                HomeworkSection(s, vm::onEvent)
                                }
                            }
                            composable(Section.Week.route) {
                                ProvideSectionEntry {
                                val vm: WeekViewModel = viewModel(factory = WeekViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                WeekSection(s, vm::onEvent) { date -> nav.openSection(Section.Schedule, date.toString()) }
                                }
                            }
                            composable(Section.Summary.route) {
                                ProvideSectionEntry {
                                val vm: SummaryViewModel = viewModel(factory = SummaryViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                SummarySection(s, vm::onEvent)
                                }
                            }
                            composable(Section.Teachers.route) {
                                ProvideSectionEntry {
                                val vm: TeachersViewModel = viewModel(factory = TeachersViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                TeachersSection(s, vm::onEvent)
                                }
                            }
                            composable(Section.Friends.route) {
                                ProvideSectionEntry {
                                val vm: FriendsViewModel = viewModel(factory = FriendsViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                FriendsSection(s, vm::onEvent)
                                }
                            }
                            composable(Section.Settings.route) {
                                ProvideSectionEntry {
                                val vm: SettingsViewModel = viewModel(factory = SettingsViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                if (host != null) {
                                    val accountVm: AccountViewModel = viewModel(owner, factory = AccountViewModel.factory(host))
                                    val account by accountVm.state.collectAsStateWithLifecycle()
                                    SettingsSection(
                                        s, vm::onEvent, update,
                                        { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.GroupPicker)) },
                                        account, accountVm::onEvent
                                    )
                                } else {
                                    SettingsSection(s, vm::onEvent, update, {
                                        shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.GroupPicker))
                                    })
                                }
                                }
                            }
                        }
                        ToastHost(container.toasts.items, onDismiss = container.toasts::dismiss, Modifier.align(Alignment.BottomCenter))
                    }
                }
                if (state.overlay == ShellOverlay.Sections) {
                    SectionsSheet(
                        current = current,
                        onPick = {
                            shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None))
                            nav.openSection(it)
                        },
                        onDismiss = { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None)) }
                    )
                }
                if (state.overlay == ShellOverlay.GroupPicker) {
                    GroupPickerSheet(
                        groups = state.groups,
                        currentId = state.groupId,
                        onPick = { id -> shellVm.onEvent(ShellEvent.PickGroup(id)) },
                        onDismiss = { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None)) }
                    )
                }
            }
        }
        }
    }
}
