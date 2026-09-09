package ru.bgtu_voenmeh.zapara.ui.week

data class WeekUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val parity: Int = 1,
    val currentParity: Int = 1,
    val days: List<WeekDayUi> = emptyList()
)

sealed interface WeekEvent {
    data class Parity(val index: Int) : WeekEvent
    data class OpenDay(val date: java.time.LocalDate) : WeekEvent
}
