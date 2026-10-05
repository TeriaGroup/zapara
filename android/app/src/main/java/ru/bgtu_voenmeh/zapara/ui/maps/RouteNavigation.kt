package ru.bgtu_voenmeh.zapara.ui.maps

/** Selection only: the VM loads coordinates and captures the thumbnail revision before publishing. */
object RouteNavigation {
    fun select(state: MapsUiState, id: Int): MapsUiState {
        val steps = state.presentation?.steps ?: return state
        val step = steps.firstOrNull { it.id == id } ?: return state
        val floor = context(steps, step)
        val changed = state.activeStepId != id || state.building != floor.building || state.floor != floor.floor
        val files = state.rasterCatalog.filterKeys { it.building == floor.building }
            .mapKeys { it.key.floor }.mapValues { it.value.file }
        return state.copy(
            activeStepId = id, building = floor.building, floor = floor.floor,
            floors = MapsComposer.floors(floor.building), showStack = false, stepsOpen = false,
            zoom = if (changed) 1f else state.zoom,
            fitGeneration = state.fitGeneration + if (changed) 1 else 0,
            planFile = state.rasterCatalog[floor]?.file, floorFiles = files,
            highlight = if (changed) null else state.highlight,
            path = MapsComposer.floorPathStrokes(state.route, floor.building, floor.floor),
            stairMarkers = MapsComposer.stairMarkers(state.route, floor.building, floor.floor)
        )
    }

    fun move(state: MapsUiState, delta: Int): MapsUiState {
        if (delta != -1 && delta != 1) return state
        val steps = state.presentation?.steps ?: return state
        val index = steps.indexOfFirst { it.id == state.activeStepId }
        if (index < 0) return state
        val next = steps.getOrNull(index + delta) ?: return state
        return select(state, next.id)
    }

    private fun context(steps: List<RouteStep>, step: RouteStep): FloorKey {
        val floor = if (step === steps.last() && step.kind != RoutePartKind.Walk) step.to else step.from
        return floor.copy(building = shownBuilding(floor.building))
    }
}
