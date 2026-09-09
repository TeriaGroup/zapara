package ru.bgtu_voenmeh.zapara.ui.widgets

import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork

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
