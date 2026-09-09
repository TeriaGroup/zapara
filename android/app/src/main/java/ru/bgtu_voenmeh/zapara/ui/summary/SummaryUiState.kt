package ru.bgtu_voenmeh.zapara.ui.summary

data class SummaryUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val segment: Int = 2,
    val tiles: SummaryTiles = SummaryTiles(0, emptyList(), emptyList(), emptyList(), emptyList())
)

sealed interface SummaryEvent {
    data class Segment(val index: Int) : SummaryEvent
}
