package ru.bgtu_voenmeh.zapara.ui.teachers

data class TeacherRowUi(val id: String, val name: String, val subjects: String, val isMine: Boolean)

data class TeachersUiState(
    val loaded: Boolean = false,
    val query: String = "",
    val onlyMine: Boolean = true,
    val list: List<TeacherRowUi> = emptyList(),
    val total: Int = 0,
    val selected: TeacherRowUi? = null,
    val details: List<TeacherDayUi> = emptyList(),
    val parityFilter: Int = 0,
    val myIds: Set<String> = emptySet()
)

sealed interface TeachersEvent {
    data class Query(val value: String) : TeachersEvent
    data class OnlyMine(val value: Boolean) : TeachersEvent
    data class Open(val id: String) : TeachersEvent
    data object Back : TeachersEvent
    data class Parity(val index: Int) : TeachersEvent
}
