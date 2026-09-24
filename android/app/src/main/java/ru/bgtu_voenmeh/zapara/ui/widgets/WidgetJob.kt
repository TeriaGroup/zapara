package ru.bgtu_voenmeh.zapara.ui.widgets

import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.time.LocalDateTime

data class WidgetPresence(
    val schedule: Boolean,
    val homework: Boolean,
    val timer: Boolean,
    val wayfinder: Boolean,
    val week: Boolean
) {
    val any: Boolean get() = schedule || homework || timer || wayfinder || week
    val needsAdvance: Boolean get() = schedule || timer || wayfinder

    fun advanceAt(
        scheduleAt: LocalDateTime?,
        timerEndsAt: LocalDateTime?,
        timerRefreshAt: LocalDateTime?,
        timerCleared: Boolean,
        wayfinderAt: LocalDateTime?
    ): LocalDateTime? = earlierRefresh(
        widgetWakeAt(scheduleAt.takeIf { schedule }, timerEndsAt, timerRefreshAt, timer, timerCleared),
        wayfinderAt.takeIf { wayfinder }
    )
}

data class WidgetJobIdentity(
    val profileId: String,
    val databaseName: String,
    val generation: Long
) {
    val isGuest: Boolean get() = profileId == GUEST_ID

    companion object {
        const val GUEST_ID = "guest"

        fun of(profile: ProfileDescriptor, generation: Long) = WidgetJobIdentity(
            profileId = if (profile.isGuest) GUEST_ID else profile.userId ?: GUEST_ID,
            databaseName = profile.databaseName,
            generation = generation
        )
    }
}

object WidgetJobs {
    fun accept(job: WidgetJobIdentity, current: WidgetJobIdentity): Boolean =
        job.profileId == current.profileId &&
            job.databaseName == current.databaseName &&
            job.generation == current.generation

    fun canApply(
        ticket: ProfileWork.Ticket,
        job: WidgetJobIdentity,
        current: WidgetJobIdentity
    ): Boolean = ticket.isCurrent && accept(job, current)
}

internal enum class WidgetFace { Schedule, Homework, Timer, Wayfinder, Week }

/** Main-thread publication barrier: a generation is ready only after all placed faces clear. */
internal class WidgetProfilePreparation {
    private var clearingIdentity: WidgetJobIdentity? = null
    private val cleared = mutableSetOf<WidgetFace>()

    fun prepare(
        identity: WidgetJobIdentity,
        current: () -> WidgetJobIdentity,
        readPresence: () -> WidgetPresence,
        clear: (WidgetFace) -> Unit,
        onFailure: (WidgetFace, Exception) -> Unit = { _, _ -> },
        beforeClear: () -> Unit = {}
    ): Boolean {
        if (!WidgetJobs.accept(identity, current())) return false
        if (clearingIdentity != identity) beforeClear()
        val placed = try {
            readPresence()
        } catch (_: Exception) {
            // Unknown placement cannot prove that a previously failed clear is no longer needed.
            return false
        }
        if (!WidgetJobs.accept(identity, current())) return false
        if (clearingIdentity != identity) {
            clearingIdentity = identity
            cleared.clear()
        }
        val required = buildList {
            if (placed.schedule) add(WidgetFace.Schedule)
            if (placed.homework) add(WidgetFace.Homework)
            if (placed.timer) add(WidgetFace.Timer)
            if (placed.wayfinder) add(WidgetFace.Wayfinder)
            if (placed.week) add(WidgetFace.Week)
        }
        for (face in required) {
            if (!WidgetJobs.accept(identity, current())) return false
            if (face in cleared) continue
            try {
                clear(face)
                if (!WidgetJobs.accept(identity, current())) return false
                cleared += face
            } catch (error: Exception) {
                onFailure(face, error)
            }
        }
        return WidgetJobs.accept(identity, current()) && required.all { it in cleared }
    }
}

object WidgetTheme {
    fun isDark(theme: String, systemNight: Boolean): Boolean = when (theme) {
        "dark" -> true
        "light" -> false
        else -> systemNight
    }
}

data class WidgetRowBind(
    val visible: Boolean,
    val primary: String,
    val secondary: String,
    val tone: String = "text2"
) {
    companion object {
        fun hidden() = WidgetRowBind(false, "", "")
        fun visible(primary: String, secondary: String, tone: String = "text2") =
            WidgetRowBind(true, primary, secondary, tone)
        fun of(row: ScheduleWidgetRow?) = if (row == null) hidden() else visible(row.name, row.meta)
        fun of(row: HomeworkWidgetRow?) = if (row == null) hidden() else visible(row.subject, row.detail, row.tone)
    }
}

object WidgetVaultRestore {
    suspend fun beforePush(isGuest: Boolean, restore: suspend () -> Unit) {
        if (isGuest) restore()
    }
}
