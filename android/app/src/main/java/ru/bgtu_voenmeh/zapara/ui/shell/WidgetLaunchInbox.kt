package ru.bgtu_voenmeh.zapara.ui.shell

import java.time.LocalDate
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

data class WidgetLaunch(val id: Long, val section: Section, val argument: String?)

class WidgetLaunchInbox {
    private val pending = MutableStateFlow<WidgetLaunch?>(null)
    val state: StateFlow<WidgetLaunch?> = pending.asStateFlow()
    private var nextId = 0L

    @Synchronized
    fun accept(section: String?, argument: String?): WidgetLaunch? {
        val destination = when (section) {
            Section.Schedule.route -> Section.Schedule
            Section.Maps.route -> Section.Maps
            Section.Homework.route -> Section.Homework
            else -> return null
        }
        val safeArgument = when (destination) {
            Section.Schedule -> argument?.takeIf { runCatching { LocalDate.parse(it) }.isSuccess }
            Section.Maps -> argument?.trim()?.take(100)?.takeIf { it.isNotEmpty() }
            else -> null
        }
        val launch = WidgetLaunch(++nextId, destination, safeArgument)
        pending.value = launch
        return launch
    }

    @Synchronized
    fun consume(id: Long) {
        if (pending.value?.id == id) pending.value = null
    }
}
