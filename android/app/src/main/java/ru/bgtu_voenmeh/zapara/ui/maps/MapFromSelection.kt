package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.campus.Node

internal data class MapFromSelection(
    val fromId: String?,
    val lastEntranceId: String?,
    val prevRoomKey: String?
)

internal suspend fun MapLoadRequests.selectFromPlace(
    current: MapFromSelection,
    chosen: Node,
    rememberEntrance: suspend (String) -> String?,
    commit: (MapFromSelection) -> Unit
) {
    // Keep the previous coherent endpoints visible until persistence has returned.
    val next = if (chosen.kind == "entrance") {
        current.copy(fromId = chosen.id, lastEntranceId = rememberEntrance(chosen.id), prevRoomKey = null)
    } else current.copy(fromId = chosen.id, prevRoomKey = chosen.room)
    ensureCurrent()
    commit(next)
}
