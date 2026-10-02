package ru.bgtu_voenmeh.zapara.ui.week

data class WeekUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val parity: Int = 1,
    val currentParity: Int = 1,
    val days: List<WeekDayUi> = emptyList(),
    val selectedDate: java.time.LocalDate = java.time.LocalDate.now(),
    val error: String? = null,
    val noSavedSchedule: Boolean = false,
    val refreshing: Boolean = false,
    val groupId: String = "",
    val profileName: String = "",
    val comparisonDays: List<WeekDayUi> = emptyList(),
    val deadlines: List<WeekHomeworkUi> = emptyList(),
    val undatedHomework: Int = 0,
    val assessments: List<AgendaLesson> = emptyList(),
    val roomAgenda: RoomAgenda? = null,
    val fetchedAt: String? = null,
    val sourceContext: String = "",
    val sourceBaseContext: String = "",
    val refreshChanges: List<WeekChange>? = null,
    val assessmentUnknownDays: Int = 0
)
data class WeekHomeworkUi(val id: Long, val subject: String, val text: String, val date: java.time.LocalDate, val done: Boolean)

sealed interface WeekEvent {
    data class RoomDate(val date: java.time.LocalDate) : WeekEvent
    data class Compare(val date: java.time.LocalDate?) : WeekEvent
    data class Parity(val index: Int) : WeekEvent
    data class OpenDay(val date: java.time.LocalDate) : WeekEvent
    data class Shift(val weeks: Int) : WeekEvent
    data class Jump(val date: java.time.LocalDate) : WeekEvent
    data object Today : WeekEvent
    data object Retry : WeekEvent
}
