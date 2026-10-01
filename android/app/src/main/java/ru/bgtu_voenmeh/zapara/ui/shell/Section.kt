package ru.bgtu_voenmeh.zapara.ui.shell

import android.net.Uri
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.navigation.NavGraph.Companion.findStartDestination
import androidx.navigation.NavHostController
import ru.bgtu_voenmeh.zapara.R

enum class Section(
    val route: String,
    @StringRes val title: Int,
    @DrawableRes val icon: Int,
    val tag: String,
    val inBar: Boolean
) {
    Schedule("schedule", R.string.nav_schedule, R.drawable.ic_calendar, "Nav.Schedule", true),
    Maps("maps", R.string.nav_maps, R.drawable.ic_map, "Nav.Maps", true),
    Homework("homework", R.string.nav_homework, R.drawable.ic_homework, "Nav.Homework", true),
    Chat("chat", R.string.nav_chat, R.drawable.ic_chat, "Nav.Chat", true),
    Week("week", R.string.nav_week, R.drawable.ic_week, "Sections.Week", false),
    Summary("summary", R.string.nav_summary, R.drawable.ic_summary, "Sections.Summary", false),
    Teachers("teachers", R.string.nav_teachers, R.drawable.ic_teachers, "Sections.Teachers", false),
    Friends("friends", R.string.nav_friends, R.drawable.ic_friends, "Sections.Friends", false),
    Community("community", R.string.nav_community, R.drawable.ic_community, "Sections.Community", false),
    Group("group", R.string.nav_group, R.drawable.ic_chat, "Sections.Group", false),
    Settings("settings", R.string.nav_settings, R.drawable.ic_settings, "Sections.Settings", false);

    val pattern: String get() = when (this) {
        Schedule -> "schedule?date={date}&time={time}&subject={subject}&widgetProfile={widgetProfile}&widgetDatabase={widgetDatabase}&widgetGroup={widgetGroup}"
        Maps -> "maps?room={room}&date={date}"
        Week -> "week?date={date}"
        Homework -> "homework?id={id}&widgetProfile={widgetProfile}&widgetDatabase={widgetDatabase}&query={query}&sourceGroup={sourceGroup}&sourceProfile={sourceProfile}"
        Teachers -> "teachers?id={id}&query={query}&sourceGroup={sourceGroup}&sourceProfile={sourceProfile}"
        Group -> "group?communityId={communityId}&conversationId={conversationId}&context={context}"
        Settings -> "settings?section={section}"
        else -> route
    }

    companion object {
        val bar: List<Section> = entries.filter { it.inBar }
        val sheet: List<Section> = entries.filter { !it.inBar }
        fun byRoute(route: String?): Section? =
            route?.substringBefore('?')?.let { r -> entries.firstOrNull { it.route == r } }
    }
}

fun NavHostController.openSection(
    section: Section, arg: String? = null, conversationId: String? = null,
    widgetScope: WidgetLaunchScope? = null, fresh: Boolean = false, sourceDate: String? = null,
    focusTime: String? = null, focusSubject: String? = null, widgetGroup: String? = null,
    detailQuery: String? = null, sourceGroup: String? = null, sourceProfile: String? = null
) {
    val dest = when {
        section == Section.Schedule && arg != null -> "schedule?date=$arg" +
            (focusTime?.let { "&time=${Uri.encode(it)}" } ?: "") +
            (focusSubject?.let { "&subject=${Uri.encode(it)}" } ?: "") +
            (widgetScope?.let { "&widgetProfile=${Uri.encode(it.profileId)}&widgetDatabase=${Uri.encode(it.databaseName)}" } ?: "") +
            (widgetGroup?.let { "&widgetGroup=${Uri.encode(it)}" } ?: "")
        section == Section.Maps && arg != null -> "maps?room=${Uri.encode(arg)}" +
            (sourceDate?.let { "&date=${Uri.encode(it)}" } ?: "")
        section == Section.Homework && homeworkWidgetId(arg) != null -> "homework?id=${homeworkWidgetId(arg)}" +
            (widgetScope?.let { "&widgetProfile=${Uri.encode(it.profileId)}&widgetDatabase=${Uri.encode(it.databaseName)}" } ?: "")
        section in setOf(Section.Homework, Section.Teachers) && detailQuery != null -> "${section.route}?query=${Uri.encode(detailQuery)}" +
            (arg?.let { "&id=${Uri.encode(it)}" } ?: "") +
            (sourceGroup?.let { "&sourceGroup=${Uri.encode(it)}" } ?: "") +
            (sourceProfile?.let { "&sourceProfile=${Uri.encode(it)}" } ?: "")
        section == Section.Group && arg != null -> "group?communityId=${Uri.encode(arg)}" +
            (conversationId?.let { "&conversationId=${Uri.encode(it)}" } ?: "")
        section == Section.Settings && arg != null -> "settings?section=${Uri.encode(arg)}"
        else -> section.route
    }
    navigate(dest) {
        if (!keepsSectionOrigin(section, arg ?: detailQuery, fresh)) {
            popUpTo(graph.findStartDestination().id) { saveState = true }
        }
        // Argument-bearing routes need a fresh entry even when the route string repeats:
        // Schedule may have moved to another day since the last launch.
        launchSingleTop = arg == null && detailQuery == null && !fresh
        restoreState = arg == null && detailQuery == null && !fresh
    }
}

internal fun keepsSectionOrigin(section: Section, argument: String?, fresh: Boolean): Boolean =
    !fresh && argument != null && section in setOf(
        Section.Schedule, Section.Maps, Section.Homework, Section.Teachers, Section.Week, Section.Group, Section.Settings
    )
