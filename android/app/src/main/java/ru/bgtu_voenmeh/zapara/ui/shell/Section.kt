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
    Week("week", R.string.nav_week, R.drawable.ic_week, "Sections.Week", false),
    Summary("summary", R.string.nav_summary, R.drawable.ic_summary, "Sections.Summary", false),
    Teachers("teachers", R.string.nav_teachers, R.drawable.ic_teachers, "Sections.Teachers", false),
    Friends("friends", R.string.nav_friends, R.drawable.ic_friends, "Sections.Friends", false),
    Community("community", R.string.nav_community, R.drawable.ic_community, "Sections.Community", false),
    Settings("settings", R.string.nav_settings, R.drawable.ic_settings, "Sections.Settings", false);

    val pattern: String get() = when (this) {
        Schedule -> "schedule?date={date}"
        Maps -> "maps?room={room}"
        else -> route
    }

    companion object {
        val bar: List<Section> = entries.filter { it.inBar }
        val sheet: List<Section> = entries.filter { !it.inBar }
        fun byRoute(route: String?): Section? =
            route?.substringBefore('?')?.let { r -> entries.firstOrNull { it.route == r } }
    }
}

fun NavHostController.openSection(section: Section, arg: String? = null) {
    val dest = when {
        section == Section.Schedule && arg != null -> "schedule?date=$arg"
        section == Section.Maps && arg != null -> "maps?room=${Uri.encode(arg)}"
        else -> section.route
    }
    navigate(dest) {
        popUpTo(graph.findStartDestination().id) { saveState = true }
        launchSingleTop = true
        restoreState = arg == null
    }
}
