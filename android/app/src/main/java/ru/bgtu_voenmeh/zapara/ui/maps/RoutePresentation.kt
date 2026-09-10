package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint

data class FloorKey(val building: String, val floor: Int)

data class RasterSize(val width: Int, val height: Int) {
    init {
        require(width > 0 && height > 0)
    }
}

enum class RoutePartKind { Walk, StairUp, StairDown, BuildingLink }
enum class RouteProblem { MissingMap, MissingCoordinates, InvalidGeometry, UnsupportedLeg }
enum class RouteMarkerKind { Start, Destination, StairDeparture, StairArrival, LinkDeparture, LinkArrival }

data class RouteMarker(
    val kind: RouteMarkerKind,
    val floor: FloorKey,
    val point: GraphPoint,
    val target: FloorKey? = null
)

data class RouteSegment(
    val legIndex: Int,
    val floor: FloorKey,
    val kind: RoutePartKind,
    val points: List<GraphPoint>,
    val problems: Set<RouteProblem> = emptySet()
)

data class RouteStep(
    val id: Int,
    val kind: RoutePartKind,
    val from: FloorKey,
    val to: FloorKey,
    val segmentIndices: List<Int>,
    val markers: List<RouteMarker>,
    val problems: Set<RouteProblem>
)

data class RoutePresentation(
    val steps: List<RouteStep>,
    val segments: List<RouteSegment>,
    val endpoints: List<RouteMarker>,
    val arrived: Boolean,
    val problems: Set<RouteProblem>
)
