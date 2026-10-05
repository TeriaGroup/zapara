package ru.bgtu_voenmeh.zapara.ui.summary

enum class SummaryDetailKind { Subject, Teacher, Room }

data class SummaryDetailTarget(val kind: SummaryDetailKind, val label: String,
    val lookup: String?, val groupId: String, val profileName: String)

data class SummaryUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val segment: Int = 2,
    val tiles: SummaryTiles = SummaryTiles(total = 0, byType = emptyList(), bySubject = emptyList(),
        byTeacher = emptyList(), rooms = emptyList(), byDay = emptyList(), byRoom = emptyList()),
    val dayDates: Map<Int, java.time.LocalDate> = emptyMap(),
    val noSavedSchedule: Boolean = false,
    val error: String? = null,
    val refreshing: Boolean = false,
    val groupId: String = "",
    val profileName: String = ""
)

sealed interface SummaryEvent {
    data class Segment(val index: Int) : SummaryEvent
    data object Retry : SummaryEvent
}
