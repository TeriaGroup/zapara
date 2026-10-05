package ru.bgtu_voenmeh.zapara.ui.week

data class WeekChange(val day: Int, val beforeDate: java.time.LocalDate, val afterDate: java.time.LocalDate,
    val removed: List<WeekRowUi>, val added: List<WeekRowUi>)

/** Compare recurring slots on corresponding weekdays, retaining duplicate multiplicity. */
internal fun compareWeeks(first: List<WeekDayUi>, second: List<WeekDayUi>): List<WeekChange> {
    fun key(row: WeekRowUi) = listOf(row.start, row.end, row.subjectRaw, row.typeRaw.ifBlank { row.type }, row.teacherRaw, row.classroomRaw)
    fun difference(a: List<WeekRowUi>, b: List<WeekRowUi>): List<WeekRowUi> {
        val remaining = b.groupingBy(::key).eachCount().toMutableMap()
        return a.filter { row -> val id = key(row); val count = remaining[id] ?: 0
            if (count == 0) true else { remaining[id] = count - 1; false } }
    }
    return first.mapNotNull { day ->
        val other = second.firstOrNull { it.dow == day.dow } ?: return@mapNotNull null
        WeekChange(day.dow, day.date, other.date, difference(day.rows, other.rows), difference(other.rows, day.rows))
    }
}
