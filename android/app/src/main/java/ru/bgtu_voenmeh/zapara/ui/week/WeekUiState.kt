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
    val profileName: String = ""
)

sealed interface WeekEvent {
    data class Parity(val index: Int) : WeekEvent
    data class OpenDay(val date: java.time.LocalDate) : WeekEvent
    data class Shift(val weeks: Int) : WeekEvent
    data object Today : WeekEvent
    data object Retry : WeekEvent
}
