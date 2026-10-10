package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.saveable.rememberSaveable
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableStateFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.res.stringResource
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
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ZaparaApplication
import ru.bgtu_voenmeh.zapara.ui.account.AccountViewModel
import ru.bgtu_voenmeh.zapara.ui.communities.CommunitiesSection
import ru.bgtu_voenmeh.zapara.ui.communities.CommunitiesViewModel
import ru.bgtu_voenmeh.zapara.ui.groups.GroupSection
import ru.bgtu_voenmeh.zapara.ui.groups.GroupViewModel
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxSection
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxViewModel
import ru.bgtu_voenmeh.zapara.ui.components.ToastHost
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsSection
import ru.bgtu_voenmeh.zapara.ui.friends.FriendsViewModel
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkSection
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkViewModel
import ru.bgtu_voenmeh.zapara.ui.homework.nextHomeworkLesson
import ru.bgtu_voenmeh.zapara.ui.calendar.AndroidCalendarShare
import ru.bgtu_voenmeh.zapara.ui.calendar.CalendarLessonExport
import ru.bgtu_voenmeh.zapara.ui.maps.MapsSection
import ru.bgtu_voenmeh.zapara.ui.maps.MapsViewModel
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleViewModel
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsSection
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsViewModel
import ru.bgtu_voenmeh.zapara.ui.summary.SummarySection
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryDetailKind
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
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.week.WeekSection
import ru.bgtu_voenmeh.zapara.ui.week.WeekViewModel

@OptIn(ExperimentalComposeUiApi::class)
@Composable
fun ZaparaApp(
    container: AppContainer,
    launch: WidgetLaunch? = null,
    onLaunchHandled: (Long) -> Unit = {}
) {
    val activity = LocalContext.current as ComponentActivity
    val app = activity.application as? ZaparaApplication
    val genFlow = app?.host?.generation ?: remember { MutableStateFlow(0L) }
    val generation by genFlow.collectAsStateWithLifecycle()
    key(generation) {
        val live = app?.container ?: container
        val owner = app?.host ?: activity
        ZaparaAppBody(live, activity, owner, app?.host, launch, onLaunchHandled)
    }
}

@OptIn(ExperimentalComposeUiApi::class)
@Composable
private fun ZaparaAppBody(
    container: AppContainer,
    activity: ComponentActivity,
    owner: androidx.lifecycle.ViewModelStoreOwner,
    host: ru.bgtu_voenmeh.zapara.AndroidProfileHost?,
    launch: WidgetLaunch?,
    onLaunchHandled: (Long) -> Unit
) {
    val shellVm: ShellViewModel = viewModel(owner, factory = ShellViewModel.factory(container))
    var widgetRestoreAttempted by remember { mutableStateOf(false) }
    suspend fun resolveWidgetLaunch(target: WidgetLaunch): WidgetLaunchResolution? = target.resolveAfterRestore(
        restore = {
            // Host construction normally restores synchronously. Retry the existing guest
            // fallback before checking a persisted scoped action; never select its account.
            if (!widgetRestoreAttempted && host?.container?.profile?.isGuest == true) {
                withContext(Dispatchers.IO) { host.restore() }
                widgetRestoreAttempted = true
            }
        },
        profile = { host?.container?.profile ?: container.profile },
        group = { withContext(Dispatchers.IO) { (host?.container ?: container).repo.settings().myGroupId } }
    )
    fun launchFeedback(problem: WidgetLaunchProblem, section: Section = Section.Homework) {
        container.toasts.show(container.app.getString(when {
            section == Section.Schedule && problem == WidgetLaunchProblem.OtherProfile -> R.string.ux60_widget_other_profile
            section == Section.Schedule && problem == WidgetLaunchProblem.OtherGroup -> R.string.ux60_widget_other_group
            section == Section.Schedule -> R.string.ux60_widget_lesson_unavailable
            problem == WidgetLaunchProblem.OtherProfile -> R.string.ux100_common_widget_other_profile
            else -> R.string.homework_widget_unavailable
        }))
    }
    suspend fun matchesSource(group: String?, profile: String?): Boolean = withContext(Dispatchers.IO) {
        !group.isNullOrBlank() && container.repo.settings().myGroupId == group &&
            container.profile.databaseName == profile
    } && (host == null || host.container === container)
    val state by shellVm.state.collectAsStateWithLifecycle()
    val update by container.update.state.collectAsStateWithLifecycle()
    val lifecycle = androidx.compose.ui.platform.LocalLifecycleOwner.current.lifecycle
    androidx.compose.runtime.DisposableEffect(lifecycle, container) {
        val observer = androidx.lifecycle.LifecycleEventObserver { _, event ->
            if (event == androidx.lifecycle.Lifecycle.Event.ON_RESUME) container.privateSync?.requestSync()
        }
        lifecycle.addObserver(observer)
        onDispose { lifecycle.removeObserver(observer) }
    }
    ZaparaTheme(choice = state.theme, motion = MotionSettings(state.animations, 1f)) {
        val motion = Zapara.motion
        val slidePx = with(LocalDensity.current) { 8.dp.roundToPx() }
        ThemeCrossfade(key = Zapara.colors.isDark, motion = motion) {
        val nav = rememberNavController()
        val appContext = androidx.compose.ui.platform.LocalContext.current.applicationContext
        val recentGroups = remember(appContext) { RecentGroups(appContext) } // #108 / AN-19
        val entry by nav.currentBackStackEntryAsState()
        val current = Section.byRoute(entry?.destination?.route) ?: Section.Schedule
        val barCurrent = if (current == Section.Group && !entry?.arguments?.getString("communityId").isNullOrBlank()) Section.Chat else current
        LaunchedEffect(launch?.id) {
            launch?.let {
                val resolved = resolveWidgetLaunch(it) ?: return@LaunchedEffect
                // Restoration can replace the whole profile-owned composition while suspended.
                if (host != null && host.container !== container) return@LaunchedEffect
                nav.openSection(it.section, resolved.argument, widgetScope = resolved.scope,
                    fresh = true, focusTime = resolved.scheduleTarget?.time,
                    focusSubject = resolved.scheduleTarget?.subject, widgetGroup = resolved.scheduleTarget?.groupId)
                resolved.problem?.let { problem -> launchFeedback(problem, it.section) }
                onLaunchHandled(it.id)
            }
        }
        val chip = state.groupName?.let { ShellLogic.chip(it, state.odd, container.copy) }
        val chipShort = state.groupName?.let { ShellLogic.chipShort(it, state.odd, container.copy) }
        val chrome = ShellChrome(chip, state.stale, state.hasGroup, chipShort) {
            shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.GroupPicker))
        }
        val themeDesc = if (Zapara.colors.isDark) stringResource(R.string.theme_dark) else stringResource(R.string.theme_light)
        BackHandler(enabled = state.overlay != ShellOverlay.None || nav.previousBackStackEntry != null || current != Section.Schedule) {
            when {
                state.overlay != ShellOverlay.None ->
                    shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None))
                else -> if (!nav.popBackStack()) nav.openSection(Section.Schedule)
            }
        }
        val conversation = remember { ConversationOpenState() }
        CompositionLocalProvider(LocalShellChrome provides chrome, LocalConversationOpen provides conversation,
            ru.bgtu_voenmeh.zapara.ui.chat.LocalAvatarStore provides container.avatars) {
            Box(Modifier.fillMaxSize()) {
                ZAppScaffold(
                    modifier = Modifier.semantics {
                        testTagsAsResourceId = true
                        stateDescription = themeDesc
                    },
                    conversation = current == Section.Chat || current == Section.Group,
                    bottomBar = {
                        if (ShellLogic.showBottomBar(current, conversation.open)) ZBottomBar(
                            current = barCurrent,
                            sectionsActive = barCurrent !in Section.bar || state.overlay == ShellOverlay.Sections,
                            homeworkBadge = state.homeworkBadge,
                            updateBadge = update.hasUpdate,
                            onSection = {
                                shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None))
                                nav.openSection(it)
                            },
                            onSections = { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.Sections)) }
                        )
                    }
                ) {
                    Column(Modifier.fillMaxSize()) {
                        if (state.error) ZCard(Modifier.fillMaxWidth().padding(Zapara.space.s)) {
                            Row(verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                                Text(stringResource(R.string.ux60_shell_projection_failed), Modifier.weight(1f),
                                    style = Zapara.typography.caption, color = Zapara.colors.text2)
                                ZButton(stringResource(R.string.maps_retry),
                                    { shellVm.onEvent(ShellEvent.RetryProjection) }, ghost = true,
                                    tag = "Shell.RetryProjection")
                            }
                        }
                        NavHost(
                            navController = nav,
                            modifier = Modifier.weight(1f),
                            startDestination = Section.Schedule.pattern,
                            enterTransition = { fadeIn(tween(motion.ms(Durations.section), easing = ZaparaEase)) + slideInHorizontally(tween(motion.ms(Durations.section), easing = ZaparaEase)) { slidePx } },
                            exitTransition = { fadeOut(tween(motion.ms(Durations.section), easing = ZaparaEase)) + slideOutHorizontally(tween(motion.ms(Durations.section), easing = ZaparaEase)) { -slidePx } },
                            popEnterTransition = { fadeIn(tween(motion.ms(Durations.section), easing = ZaparaEase)) + slideInHorizontally(tween(motion.ms(Durations.section), easing = ZaparaEase)) { -slidePx } },
                            popExitTransition = { fadeOut(tween(motion.ms(Durations.section), easing = ZaparaEase)) + slideOutHorizontally(tween(motion.ms(Durations.section), easing = ZaparaEase)) { slidePx } }
                        ) {
                            composable(
                                Section.Schedule.pattern,
                                arguments = listOf(
                                    navArgument("date") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("time") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("subject") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("lessonKey") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("widgetProfile") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("widgetDatabase") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("widgetGroup") { type = NavType.StringType; nullable = true; defaultValue = null })
                            ) { dest ->
                                ProvideSectionEntry {
                                val date = dest.arguments?.getString("date")
                                val widgetProfile = dest.arguments?.getString("widgetProfile")
                                val widgetDatabase = dest.arguments?.getString("widgetDatabase")
                                val widgetGroup = dest.arguments?.getString("widgetGroup")
                                val focusTime = dest.arguments?.getString("time")
                                val focusSubject = dest.arguments?.getString("subject")
                                val academicKey = dest.arguments?.getString("lessonKey")
                                val scoped = widgetProfile != null || widgetDatabase != null || widgetGroup != null
                                val vm: ScheduleViewModel = viewModel(factory = ScheduleViewModel.factory(container, if (scoped) null else date))
                                val s by vm.state.collectAsStateWithLifecycle()
                                var scopedValid by remember(dest.id) { mutableStateOf(false) }
                                var scopedFailureShown by remember(dest.id) { mutableStateOf(false) }
                                var rowChecked by remember(dest.id) { mutableStateOf(false) }
                                LaunchedEffect(date, s.loaded, s.groupId, scoped, launch?.id) {
                                    if (scoped && (!s.loaded || launch != null)) return@LaunchedEffect
                                    val argument = if (scoped) {
                                        val request = WidgetLaunchInbox().accept("schedule", date, widgetProfile,
                                            widgetDatabase, widgetGroup, focusTime, focusSubject) ?: return@LaunchedEffect
                                        val resolved = resolveWidgetLaunch(request) ?: return@LaunchedEffect
                                        if (host != null && host.container !== container) return@LaunchedEffect
                                        scopedValid = resolved.problem == null
                                        resolved.problem?.let {
                                            if (!scopedFailureShown) launchFeedback(it, Section.Schedule)
                                            scopedFailureShown = true
                                            vm.onEvent(ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleEvent.Today)
                                        }
                                        resolved.argument
                                    } else date
                                    argument?.let { runCatching { java.time.LocalDate.parse(it) }.getOrNull() }?.let {
                                        vm.onEvent(ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleEvent.Select(it))
                                    }
                                }
                                LaunchedEffect(scopedValid, s.pages) {
                                    if (!scopedValid || rowChecked || focusTime == null ||
                                        focusSubject == null || widgetGroup == null) return@LaunchedEffect
                                    val day = date?.let { runCatching { java.time.LocalDate.parse(it) }.getOrNull() } ?: return@LaunchedEffect
                                    val page = s.pages[day] ?: return@LaunchedEffect
                                    rowChecked = true
                                    if (page.lessons.none { it.timeStart == focusTime && it.subjectNorm == focusSubject })
                                        launchFeedback(WidgetLaunchProblem.InvalidTarget, Section.Schedule)
                                }
                                val dayShareScope = rememberCoroutineScope()
                                ScheduleSection(s, vm::onEvent, onDiscuss = { context -> nav.navigate("group?context=${android.net.Uri.encode(context)}") },
                                    onWeek = { selected -> nav.navigate("week?date=$selected") },
                                    onShareDay = { page -> dayShareScope.launch {
                                        if (!matchesSource(s.groupId, s.profileName)) return@launch
                                        val name = activity.getString(R.string.ux300_android_calendar_day_name,
                                            page.date.format(java.time.format.DateTimeFormatter.ofPattern(
                                                "d MMMM yyyy", java.util.Locale.forLanguageTag("ru"))))
                                        val text = CalendarLessonExport.plainText(name,
                                            CalendarLessonExport.day(page),
                                            activity.getString(R.string.ux300_android_no_lessons))
                                        activity.startActivity(android.content.Intent.createChooser(
                                            AndroidCalendarShare.textIntent(text),
                                            activity.getString(R.string.ux300_android_share_day)))
                                    } },
                                    focusTime = focusTime.takeIf { !scoped || scopedValid },
                                    focusSubject = focusSubject.takeIf { !scoped || scopedValid },
                                    academicKey = academicKey.takeIf { !scoped || scopedValid }) { room ->
                                    nav.openSection(Section.Maps, room, sourceDate = s.selected.toString())
                                }
                                }
                            }
                            composable(
                                Section.Maps.pattern,
                                arguments = listOf(
                                    navArgument("room") { type = NavType.StringType; nullable = true; defaultValue = null },
                                    navArgument("date") { type = NavType.StringType; nullable = true; defaultValue = null })
                            ) { dest ->
                                ProvideSectionEntry {
                                val room = dest.arguments?.getString("room")
                                val vm: MapsViewModel = viewModel(factory = MapsViewModel.factory(container, room))
                                val s by vm.state.collectAsStateWithLifecycle()
                                LaunchedEffect(room) {
                                    if (!room.isNullOrBlank()) vm.onEvent(ru.bgtu_voenmeh.zapara.ui.maps.MapsEvent.ShowRoom(room))
                                    else vm.onEvent(ru.bgtu_voenmeh.zapara.ui.maps.MapsEvent.Browse)
                                }
                                val sourceDate = dest.arguments?.getString("date")?.let {
                                    runCatching { java.time.LocalDate.parse(it) }.getOrNull()
                                }
                                MapsSection(s, vm::onEvent, onBackToLesson = sourceDate?.let { date ->
                                    {
                                        if (Section.byRoute(nav.previousBackStackEntry?.destination?.route) == Section.Schedule)
                                            nav.popBackStack()
                                        else nav.openSection(Section.Schedule, date.toString(), fresh = true)
                                    }
                                })
                                }
                            }
                            composable(Section.Homework.pattern, arguments = listOf(
                                navArgument("id") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("widgetProfile") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("widgetDatabase") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("query") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("sourceGroup") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("sourceProfile") { type = NavType.StringType; nullable = true; defaultValue = null }
                            )) { dest ->
                                ProvideSectionEntry {
                                // Preserve an active draft across tabs and repeated widget routes.
                                // The profile host clears this store when the account changes.
                                val vm: HomeworkViewModel = viewModel(owner, factory = HomeworkViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                val targetId = dest.arguments?.getString("id")
                                val widgetProfile = dest.arguments?.getString("widgetProfile")
                                val widgetDatabase = dest.arguments?.getString("widgetDatabase")
                                val detailQuery = dest.arguments?.getString("query")
                                var detailHandled by rememberSaveable(dest.id) { mutableStateOf(false) }
                                LaunchedEffect(detailQuery, s.loaded) {
                                    if (detailQuery == null || detailHandled || !s.loaded) return@LaunchedEffect
                                    detailHandled = true
                                    if (matchesSource(dest.arguments?.getString("sourceGroup"), dest.arguments?.getString("sourceProfile"))) {
                                        vm.onEvent(ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEvent.BrowseFilter(
                                            ru.bgtu_voenmeh.zapara.ui.homework.HomeworkCompletionFilter.All))
                                        vm.onEvent(ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEvent.BrowseQuery(detailQuery))
                                    }
                                }
                                var targetHandled by rememberSaveable(dest.id, targetId, widgetProfile, widgetDatabase) { mutableStateOf(false) }
                                LaunchedEffect(targetId, widgetProfile, widgetDatabase, s.loaded, launch?.id) {
                                    // A newer widget launch takes precedence over a restored old route.
                                    if (launch != null || targetHandled || targetId == null || !s.loaded) return@LaunchedEffect
                                    val scoped = widgetProfile != null || widgetDatabase != null
                                    val argument = if (scoped) {
                                        val scope = WidgetLaunchScope.parse(widgetProfile, widgetDatabase)
                                        val resolved = resolveWidgetLaunch(WidgetLaunch(0, Section.Homework, targetId,
                                            scope, invalidTarget = scope == null)) ?: return@LaunchedEffect
                                        if (host != null && host.container !== container) return@LaunchedEffect
                                        resolved.problem?.let(::launchFeedback)
                                        resolved.argument
                                    } else targetId
                                    targetHandled = true
                                    homeworkWidgetId(argument)?.let { vm.onEvent(ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEvent.Edit(it)) }
                                }
                                val homeworkNavigation = rememberCoroutineScope()
                                HomeworkSection(s, vm::onEvent) { subject ->
                                    val expectedProfile = container.profile.databaseName
                                    homeworkNavigation.launch {
                                        val (group, target) = withContext(Dispatchers.IO) {
                                            val settings = container.repo.settings()
                                            val group = settings.myGroupId.orEmpty()
                                            group to nextHomeworkLesson(container.ownLessons(),
                                                ru.bgtu_voenmeh.zapara.data.SchedCtx(group, settings.periodStart,
                                                    settings.weekCount, settings.parityInvert),
                                                subject, container.clock())
                                        }
                                        val currentGroup = withContext(Dispatchers.IO) {
                                            container.repo.settings().myGroupId.orEmpty()
                                        }
                                        if (expectedProfile != container.profile.databaseName || group != currentGroup ||
                                            (container.app as? ZaparaApplication)?.container !== container) return@launch
                                        if (target == null) container.toasts.show(
                                            container.app.getString(R.string.ux300_android_no_next_subject_lesson),
                                            ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Bad)
                                        else nav.openSection(Section.Schedule, target.date.toString(),
                                            focusTime = target.time, focusSubject = target.subjectNorm)
                                    }
                                }
                                }
                            }
                            composable(Section.Week.pattern, arguments = listOf(navArgument("date") { type = NavType.StringType; nullable = true; defaultValue = null })) { dest ->
                                ProvideSectionEntry {
                                val vm: WeekViewModel = viewModel(factory = WeekViewModel.factory(container, dest.arguments?.getString("date")))
                                val s by vm.state.collectAsStateWithLifecycle()
                                val weekShareScope = rememberCoroutineScope()
                                WeekSection(s, vm::onEvent,
                                    onOpenAgendaMap = { raw -> weekShareScope.launch {
                                        if (matchesSource(s.groupId, s.profileName)) nav.openSection(Section.Maps, raw)
                                    } },
                                    onOpenAcademicLesson = { date, lesson -> weekShareScope.launch {
                                        if (!matchesSource(s.groupId, s.profileName)) return@launch
                                        val valid = withContext(Dispatchers.IO) {
                                            val prefs = container.repo.settings()
                                            val current = ru.bgtu_voenmeh.zapara.data.Schedule.lessonsForDate(container.ownLessons(),
                                                s.groupId, date, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                                            current.count { ru.bgtu_voenmeh.zapara.ui.week.academicLessonKey(it) ==
                                                ru.bgtu_voenmeh.zapara.ui.week.academicLessonKey(lesson) } == 1 && current.any { it == lesson }
                                        }
                                        if (!matchesSource(s.groupId, s.profileName)) return@launch
                                        if (valid) nav.openSection(Section.Schedule, date.toString(),
                                            lessonKey = ru.bgtu_voenmeh.zapara.ui.week.academicLessonKey(lesson))
                                        else container.toasts.show(activity.getString(R.string.ux300_agenda_lesson_changed),
                                            ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Bad)
                                    } },
                                    onOpenHomework = { id -> weekShareScope.launch {
                                        if (matchesSource(s.groupId, s.profileName)) nav.openSection(Section.Homework, id.toString())
                                    } },
                                    onOpenLesson = { date, time, subject -> nav.openSection(Section.Schedule,
                                        date.toString(), focusTime = time, focusSubject = subject) },
                                    onExportIcs = { week -> weekShareScope.launch {
                                        if (!matchesSource(week.groupId, week.profileName) || week.days.size != 7)
                                            return@launch
                                        val name = activity.getString(R.string.ux300_android_calendar_week_name,
                                            week.days.first().date.toString(), week.days.last().date.toString(),
                                            week.groupId)
                                        try {
                                            val result = withContext(Dispatchers.IO) {
                                                CalendarLessonExport.ics(CalendarLessonExport.week(week.days),
                                                    week.groupId, name, java.time.Instant.now())
                                            }
                                            if (result.eventCount == 0) {
                                                container.toasts.show(activity.getString(R.string.ux300_android_calendar_no_events),
                                                    ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Bad)
                                                return@launch
                                            }
                                            val uri = withContext(Dispatchers.IO) {
                                                AndroidCalendarShare.writeIcs(activity, result)
                                            }
                                            if (!matchesSource(week.groupId, week.profileName)) return@launch
                                            val send = AndroidCalendarShare.icsIntent(activity, uri)
                                            activity.startActivity(android.content.Intent.createChooser(send,
                                                activity.getString(R.string.ux300_android_export_ics)).apply {
                                                clipData = send.clipData
                                                addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION)
                                            })
                                        } catch (cancelled: kotlinx.coroutines.CancellationException) {
                                            throw cancelled
                                        } catch (_: Exception) {
                                            container.toasts.show(activity.getString(R.string.ux300_android_calendar_failed),
                                                ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Bad)
                                        }
                                    } },
                                    onShareText = { week -> weekShareScope.launch {
                                        if (!matchesSource(week.groupId, week.profileName) || week.days.size != 7)
                                            return@launch
                                        val name = activity.getString(R.string.ux300_android_calendar_week_name,
                                            week.days.first().date.toString(), week.days.last().date.toString(),
                                            week.groupId)
                                        val text = CalendarLessonExport.plainText(name,
                                            CalendarLessonExport.week(week.days),
                                            activity.getString(R.string.ux300_android_no_lessons))
                                        activity.startActivity(android.content.Intent.createChooser(
                                            AndroidCalendarShare.textIntent(text),
                                            activity.getString(R.string.ux300_android_share_text)))
                                    } },
                                    onOpenDay = { date -> nav.openSection(Section.Schedule, date.toString()) })
                                }
                            }
                            composable(Section.Summary.route) {
                                ProvideSectionEntry {
                                val vm: SummaryViewModel = viewModel(factory = SummaryViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                val detailNavigation = rememberCoroutineScope()
                                SummarySection(s, vm::onEvent,
                                    onOpenDay = { date -> nav.openSection(Section.Schedule, date.toString()) },
                                    onOpenDetail = { target -> detailNavigation.launch {
                                        if (!matchesSource(target.groupId, target.profileName)) return@launch
                                        when (target.kind) {
                                            SummaryDetailKind.Subject -> nav.openSection(Section.Homework,
                                                detailQuery = target.lookup ?: target.label,
                                                sourceGroup = target.groupId, sourceProfile = target.profileName)
                                            SummaryDetailKind.Teacher -> nav.openSection(Section.Teachers, target.lookup,
                                                detailQuery = if (target.lookup != null) "" else target.label, sourceGroup = target.groupId,
                                                sourceProfile = target.profileName)
                                            SummaryDetailKind.Room -> if (target.lookup != null)
                                                nav.openSection(Section.Maps, target.lookup) else nav.navigate(Section.Maps.route)
                                        }
                                    } })
                                }
                            }
                            composable(Section.Teachers.pattern, arguments = listOf(
                                navArgument("id") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("query") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("sourceGroup") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("sourceProfile") { type = NavType.StringType; nullable = true; defaultValue = null }
                            )) { dest ->
                                ProvideSectionEntry {
                                val vm: TeachersViewModel = viewModel(factory = TeachersViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                val detailQuery = dest.arguments?.getString("query")
                                val teacherId = dest.arguments?.getString("id")
                                var detailPrepared by rememberSaveable(dest.id) { mutableStateOf(false) }
                                var detailOpened by rememberSaveable(dest.id) { mutableStateOf(false) }
                                LaunchedEffect(detailQuery, s.loaded) {
                                    if (detailQuery == null || detailPrepared || !s.loaded) return@LaunchedEffect
                                    if (!matchesSource(dest.arguments?.getString("sourceGroup"), dest.arguments?.getString("sourceProfile"))) {
                                        detailPrepared = true; detailOpened = true
                                        return@LaunchedEffect
                                    }
                                    detailPrepared = true
                                    vm.onEvent(ru.bgtu_voenmeh.zapara.ui.teachers.TeachersEvent.OnlyMine(false))
                                    vm.onEvent(ru.bgtu_voenmeh.zapara.ui.teachers.TeachersEvent.Query(detailQuery))
                                }
                                LaunchedEffect(detailPrepared, s.searching, s.appliedQuery, s.list) {
                                    if (!detailPrepared || detailOpened || s.searching || s.appliedQuery != detailQuery) return@LaunchedEffect
                                    detailOpened = true
                                    if (teacherId != null && s.list.any { it.id == teacherId } &&
                                        matchesSource(dest.arguments?.getString("sourceGroup"), dest.arguments?.getString("sourceProfile")))
                                        vm.onEvent(ru.bgtu_voenmeh.zapara.ui.teachers.TeachersEvent.Open(teacherId))
                                }
                                val teacherNavigation = rememberCoroutineScope()
                                TeachersSection(s, vm::onEvent, onOpenOwnDay = { date, expectedGroup, expectedProfile ->
                                    teacherNavigation.launch {
                                        val valid = withContext(Dispatchers.IO) {
                                            expectedGroup.isNotBlank() &&
                                                container.repo.settings().myGroupId == expectedGroup &&
                                                container.profile.databaseName == expectedProfile
                                        }
                                        if (valid && (container.app as? ZaparaApplication)?.container === container)
                                            nav.openSection(Section.Schedule, date.toString())
                                    }
                                }, onOpenMap = { room, expectedGroup, expectedProfile ->
                                    teacherNavigation.launch {
                                        val valid = withContext(Dispatchers.IO) {
                                            expectedGroup.isNotBlank() &&
                                                container.repo.settings().myGroupId == expectedGroup &&
                                                container.profile.databaseName == expectedProfile
                                        }
                                        if (valid && (container.app as? ZaparaApplication)?.container === container)
                                            nav.openSection(Section.Maps, room)
                                    }
                                })
                                }
                            }
                            composable(Section.Friends.route) {
                                ProvideSectionEntry {
                                val vm: FriendsViewModel = viewModel(factory = FriendsViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                val encounterNavigation = rememberCoroutineScope()
                                FriendsSection(s, vm::onEvent) { encounter, expectedGroup, expectedProfile ->
                                    encounterNavigation.launch {
                                        val valid = withContext(Dispatchers.IO) {
                                            expectedGroup.isNotBlank() &&
                                                container.repo.settings().myGroupId == expectedGroup &&
                                                container.profile.databaseName == expectedProfile
                                        }
                                        if (valid && (container.app as? ZaparaApplication)?.container === container)
                                            nav.openSection(Section.Schedule, encounter.date.toString(),
                                                focusTime = encounter.time, focusSubject = encounter.subject)
                                    }
                                }
                                }
                            }
                            composable(Section.Community.route) {
                                ProvideSectionEntry {
                                val vm: CommunitiesViewModel = viewModel(factory = CommunitiesViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                CommunitiesSection(s, vm::onEvent) { nav.openSection(Section.Settings, "account") }
                                }
                            }
                            composable(Section.Chat.route) {
                                ProvideSectionEntry {
                                val vm: InboxViewModel = viewModel(factory = InboxViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                InboxSection(s, vm::onEvent, onOpenGroup = { communityId, conversationId ->
                                    nav.openSection(Section.Group, communityId, conversationId)
                                }, onOpenAccount = { nav.openSection(Section.Settings, "account") })
                                }
                            }
                            composable(Section.Group.pattern, arguments = listOf(
                                navArgument("communityId") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("conversationId") { type = NavType.StringType; nullable = true; defaultValue = null },
                                navArgument("context") { type = NavType.StringType; nullable = true; defaultValue = null }
                            )) { dest ->
                                ProvideSectionEntry {
                                val communityId = dest.arguments?.getString("communityId")?.takeIf { it.isNotBlank() }
                                val conversationId = dest.arguments?.getString("conversationId")?.takeIf { it.isNotBlank() }
                                val vm: GroupViewModel = viewModel(factory = GroupViewModel.factory(container, communityId, conversationId, dest.arguments?.getString("context")))
                                val s by vm.state.collectAsStateWithLifecycle()
                                GroupSection(s, vm::onEvent,
                                    onReturnToInbox = if (conversationId != null && s.activeConversationId == conversationId &&
                                        s.activeTopicId == null && !s.showTrusted && s.spacePanel == null) {
                                        { if (!nav.popBackStack()) nav.openSection(Section.Chat) }
                                    } else null) { id -> nav.navigate("homework?id=$id") }
                                }
                            }
                            composable(Section.Settings.pattern, arguments = listOf(
                                navArgument("section") { type = NavType.StringType; nullable = true; defaultValue = null }
                            )) { dest ->
                                ProvideSectionEntry {
                                val initialSection = dest.arguments?.getString("section")?.takeIf { it == "account" }
                                val vm: SettingsViewModel = viewModel(factory = SettingsViewModel.factory(container))
                                val s by vm.state.collectAsStateWithLifecycle()
                                if (host != null) {
                                    val accountVm: AccountViewModel = viewModel(owner, factory = AccountViewModel.factory(host))
                                    val account by accountVm.state.collectAsStateWithLifecycle()
                                    SettingsSection(
                                        s, vm::onEvent, update,
                                        { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.GroupPicker)) },
                                        account, accountVm::onEvent, initialSection
                                    )
                                } else {
                                    SettingsSection(s, vm::onEvent, update, {
                                        shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.GroupPicker))
                                    }, initialSection = initialSection)
                                }
                                }
                            }
                        }
                    }
                        ToastHost(container.toasts.items, onDismiss = container.toasts::dismiss,
                            Modifier.align(Alignment.BottomCenter), onAction = container.toasts::invokeAction)
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
                        onPick = { id -> recentGroups.push(id); shellVm.onEvent(ShellEvent.PickGroup(id)) },
                        onDismiss = { shellVm.onEvent(ShellEvent.Overlay(ShellOverlay.None)) },
                        busy = state.groupPickPending, error = state.groupPickError,
                        onRetry = { shellVm.onEvent(ShellEvent.RetryGroupPick) },
                        recentIds = remember(state.overlay) { recentGroups.ids() }
                    )
                }
            }
        }
        }
    }
}
