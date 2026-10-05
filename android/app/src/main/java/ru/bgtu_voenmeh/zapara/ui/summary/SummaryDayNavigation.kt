package ru.bgtu_voenmeh.zapara.ui.summary

import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import java.time.LocalDate

internal object SummaryDayNavigation {
    fun date(dayOfWeek: Int, segment: Int, today: LocalDate, context: SchedCtx): LocalDate? {
        if (dayOfWeek !in 1..7) return null
        for (offset in 0..(7 * maxOf(2, context.weekCount * 2))) {
            val date = today.plusDays(offset.toLong())
            if (date.dayOfWeek.value != dayOfWeek) continue
            if (segment == 2 || (if (Parity.isOddWeek(date, context.periodStart, context.weekCount, context.invert)) 1 else 2) == segment + 1)
                return date
        }
        return null
    }
}
