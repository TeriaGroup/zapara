package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File

data class MapsUiState(
    val loaded: Boolean = false,
    val hasGroup: Boolean = false,
    val buildings: List<String> = listOf("ГК", "УЛК"),
    val building: String = "ГК",
    val floors: List<Int> = (1..4).toList(),
    val floor: Int = 1,
    val planFile: File? = null,
    val highlight: HighlightUi? = null,
    val contextLine: String = "",
    val mode: MapMode = MapMode.None,
    val remoteNote: String? = null,
    val note: String? = null,
    val fullscreen: Boolean = false,
    val zoom: Float = 1f,
    val remote: Boolean = false
)

sealed interface MapsEvent {
    data class PickBuilding(val index: Int) : MapsEvent
    data class PickFloor(val n: Int) : MapsEvent
    data class ShowRoom(val classroomRaw: String) : MapsEvent
    data object ToNext : MapsEvent
    data object ZoomIn : MapsEvent
    data object ZoomOut : MapsEvent
    data object Fit : MapsEvent
    data class Fullscreen(val on: Boolean) : MapsEvent
    data class Transform(val zoom: Float) : MapsEvent
}
