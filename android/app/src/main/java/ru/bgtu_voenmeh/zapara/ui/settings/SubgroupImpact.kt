package ru.bgtu_voenmeh.zapara.ui.settings

import ru.bgtu_voenmeh.zapara.data.*
import ru.bgtu_voenmeh.zapara.ui.week.*
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate

data class SubgroupImpact(val event: SettingsEvent.Subgroup, val context: SchedCtx,
    val beforeChoices: Map<String, String>, val source: List<Lesson>, val changes: List<WeekChange>, val unknownDays: Int = 0,
    val weekStart: LocalDate = LocalDate.now()) {
    fun stillMatches(profile: String, ctx: SchedCtx, choices: Map<String, String>, lessons: List<Lesson>): Boolean =
        event.profileName == profile && context == ctx && beforeChoices == choices && source == lessons
}

internal fun subgroupImpact(event: SettingsEvent.Subgroup, ctx: SchedCtx, choices: Map<String, String>,
    raw: List<Lesson>, today: LocalDate, copy: UiCopy): SubgroupImpact {
    val next = if (choices[event.streamId] == event.optionId) choices - event.streamId
        else choices + (event.streamId to event.optionId)
    val from = today.minusDays((today.dayOfWeek.value - 1).toLong())
    fun week(picks: Map<String, String>) = WeekComposer.compose(WeekNavigation.parity(from, ctx), Subgroups.visible(raw, picks),
        { _, _ -> "" }, ctx, today, copy, from).map { if (it.date.isBefore(ctx.periodStart)) it.copy(rows = emptyList()) else it }
    return SubgroupImpact(event, ctx, choices.toMap(), raw.toList(), compareWeeks(week(choices), week(next))
        .filter { it.removed.isNotEmpty() || it.added.isNotEmpty() },
        (0L..6L).count { from.plusDays(it).isBefore(ctx.periodStart) }, from)
}
