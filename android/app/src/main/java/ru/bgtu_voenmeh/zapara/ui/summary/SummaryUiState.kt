package ru.bgtu_voenmeh.zapara.ui.summary

data class SummaryUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val segment: Int = 2,
    val tiles: SummaryTiles = SummaryTiles(total = 0, byType = emptyList(), bySubject = emptyList(),
        byTeacher = emptyList(), rooms = emptyList(), byDay = emptyList(), byRoom = emptyList())
)

sealed interface SummaryEvent {
    data class Segment(val index: Int) : SummaryEvent
}
