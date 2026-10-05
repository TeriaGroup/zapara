package ru.bgtu_voenmeh.zapara.ui.teachers

data class TeacherRowUi(val id: String, val name: String, val subjects: String, val isMine: Boolean,
    val department: String = "")

data class TeachersUiState(
    val loaded: Boolean = false,
    val query: String = "",
    val onlyMine: Boolean = true,
    val appliedQuery: String = "",
    val appliedOnlyMine: Boolean = true,
    val searching: Boolean = false,
    val list: List<TeacherRowUi> = emptyList(),
    val total: Int = 0,
    val selected: TeacherRowUi? = null,
    val details: List<TeacherDayUi> = emptyList(),
    val parityFilter: Int = 0,
    val myIds: Set<String> = emptySet(),
    val loadError: String? = null,
    val detailsLoading: Boolean = false,
    val groupId: String = "",
    val profileName: String = ""
)

sealed interface TeachersEvent {
    data class Query(val value: String) : TeachersEvent
    data class OnlyMine(val value: Boolean) : TeachersEvent
    data class Open(val id: String) : TeachersEvent
    data object Back : TeachersEvent
    data class Parity(val index: Int) : TeachersEvent
    data object Retry : TeachersEvent
}

internal data class TeacherDetailRequest(val ticket: Int, val teacherId: String,
    val groupId: String = "", val profileName: String = "", val groupEpoch: Long = 0) {
    fun matches(ticket: Int, selected: TeacherRowUi?, groupId: String = this.groupId,
        profileName: String = this.profileName, groupEpoch: Long = this.groupEpoch): Boolean =
        this.ticket == ticket && selected?.id == teacherId && this.groupId == groupId &&
            this.profileName == profileName && this.groupEpoch == groupEpoch
}
