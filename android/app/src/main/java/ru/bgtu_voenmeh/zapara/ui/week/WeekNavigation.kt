package ru.bgtu_voenmeh.zapara.ui.week

import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate

internal object WeekNavigation {
    fun parity(date: LocalDate, ctx: SchedCtx): Int =
        if (Parity.isOddWeek(date, ctx.periodStart, ctx.weekCount, ctx.invert)) 1 else 2

    fun chooseParity(date: LocalDate, requested: Int, ctx: SchedCtx): LocalDate {
        if (parity(date, ctx) == requested) return date
        return (1..maxOf(2, ctx.weekCount * 2)).asSequence().map { date.plusWeeks(it.toLong()) }
            .firstOrNull { parity(it, ctx) == requested } ?: date
    }
}

internal class WeekParityHistory {
    private val inspected = HashMap<Int, LocalDate>()

    fun reset(date: LocalDate, context: SchedCtx) {
        inspected.clear()
        inspected[WeekNavigation.parity(date, context)] = date
    }

    fun choose(date: LocalDate, requested: Int, context: SchedCtx): LocalDate {
        val current = WeekNavigation.parity(date, context)
        inspected[current] = date
        val remembered = inspected[requested]
        val chosen = remembered?.takeIf { kotlin.math.abs(java.time.temporal.ChronoUnit.WEEKS.between(date, it)) <= 1 }
            ?: WeekNavigation.chooseParity(date, requested, context)
        inspected[requested] = chosen
        return chosen
    }
}
