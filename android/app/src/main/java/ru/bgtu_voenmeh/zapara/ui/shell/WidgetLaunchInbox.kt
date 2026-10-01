package ru.bgtu_voenmeh.zapara.ui.shell

import java.time.LocalDate
import java.time.LocalTime
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor

/** Stable local identity: widget actions can outlive a process, so generation is not part of it. */
data class WidgetLaunchScope(val profileId: String, val databaseName: String) {
    fun matches(profile: ProfileDescriptor): Boolean = this == of(profile)

    companion object {
        fun of(profile: ProfileDescriptor) = WidgetLaunchScope(
            if (profile.isGuest) "guest" else profile.userId.orEmpty(), profile.databaseName)

        fun parse(profileId: String?, databaseName: String?): WidgetLaunchScope? =
            if (profileId.isNullOrBlank() || databaseName.isNullOrBlank() ||
                profileId.length > 128 || databaseName.length > 256) null
            else WidgetLaunchScope(profileId, databaseName)
    }
}

internal fun homeworkWidgetId(argument: String?): Long? = argument
    ?.takeIf { it.isNotEmpty() && it.all { char -> char in '0'..'9' } }
    ?.toLongOrNull()?.takeIf { it > 0 }

enum class WidgetLaunchProblem { InvalidTarget, OtherProfile, OtherGroup }

data class ScheduleWidgetTarget(val groupId: String, val time: String, val subject: String)

data class WidgetLaunchResolution(
    val argument: String?, val scope: WidgetLaunchScope? = null, val problem: WidgetLaunchProblem? = null,
    val scheduleTarget: ScheduleWidgetTarget? = null
)

data class WidgetLaunch(
    val id: Long, val section: Section, val argument: String?,
    val scope: WidgetLaunchScope? = null, val invalidTarget: Boolean = false,
    val scheduleTarget: ScheduleWidgetTarget? = null
) {
    fun resolve(profile: ProfileDescriptor?, currentGroup: String? = null): WidgetLaunchResolution? {
        if (profile == null) return null
        if (invalidTarget) return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.InvalidTarget)
        if (scope != null && !scope.matches(profile))
            return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.OtherProfile)
        if (section == Section.Schedule && (scheduleTarget != null || invalidTarget)) {
            if (invalidTarget || argument == null || scope == null || scheduleTarget == null)
                return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.InvalidTarget)
            if (!scope.matches(profile)) return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.OtherProfile)
            if (scheduleTarget.groupId != currentGroup)
                return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.OtherGroup)
            return WidgetLaunchResolution(argument, scope, scheduleTarget = scheduleTarget)
        }
        if (section != Section.Homework || argument == null)
            return WidgetLaunchResolution(argument, scope)
        if (invalidTarget || homeworkWidgetId(argument) == null || scope == null)
            return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.InvalidTarget)
        if (!scope.matches(profile)) return WidgetLaunchResolution(null, problem = WidgetLaunchProblem.OtherProfile)
        return WidgetLaunchResolution(argument, scope)
    }

    /** Read the published profile only after normal vault restoration has had a chance to finish. */
    suspend fun resolveAfterRestore(restore: suspend () -> Unit, profile: () -> ProfileDescriptor?,
        group: suspend () -> String? = { null }): WidgetLaunchResolution? {
        if (scope != null) restore()
        return resolve(profile(), if (scheduleTarget != null) group() else null)
    }
}

class WidgetLaunchInbox {
    private val pending = MutableStateFlow<WidgetLaunch?>(null)
    val state: StateFlow<WidgetLaunch?> = pending.asStateFlow()
    private var nextId = 0L

    @Synchronized
    fun accept(section: String?, argument: String?, profileId: String? = null, databaseName: String? = null,
        scheduleGroup: String? = null, scheduleTime: String? = null, scheduleSubject: String? = null): WidgetLaunch? {
        val destination = when (section) {
            Section.Schedule.route -> Section.Schedule
            Section.Maps.route -> Section.Maps
            Section.Homework.route -> Section.Homework
            else -> return null
        }
        val safeArgument = when (destination) {
            Section.Schedule -> argument?.takeIf { runCatching { LocalDate.parse(it) }.isSuccess }
            Section.Maps -> argument?.trim()?.take(100)?.takeIf { it.isNotEmpty() }
            Section.Homework -> homeworkWidgetId(argument)?.toString()
            else -> null
        }
        val scheduleRequested = destination == Section.Schedule &&
            (scheduleGroup != null || scheduleTime != null || scheduleSubject != null)
        val target = if (scheduleRequested && !scheduleGroup.isNullOrBlank() && scheduleGroup.length <= 128 &&
            !scheduleSubject.isNullOrBlank() && scheduleSubject.length <= 256 &&
            scheduleTime != null && scheduleTime.matches(Regex("[0-2][0-9]:[0-5][0-9]")) &&
            runCatching { LocalTime.parse(scheduleTime) }.isSuccess)
            ScheduleWidgetTarget(scheduleGroup, scheduleTime, scheduleSubject) else null
        val scopeRequested = profileId != null || databaseName != null
        val scope = WidgetLaunchScope.parse(profileId, databaseName)
        val requestedTarget = destination == Section.Homework && argument != null || scheduleRequested
        val launch = WidgetLaunch(++nextId, destination, safeArgument, scope,
            invalidTarget = scopeRequested && scope == null ||
                requestedTarget && (safeArgument == null || scope == null || scheduleRequested && target == null),
            scheduleTarget = target)
        pending.value = launch
        return launch
    }

    @Synchronized
    fun consume(id: Long) {
        if (pending.value?.id == id) pending.value = null
    }
}
