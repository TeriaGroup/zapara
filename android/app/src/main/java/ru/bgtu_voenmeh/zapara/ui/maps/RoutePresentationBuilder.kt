package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.campus.GraphPoint
import ru.bgtu_voenmeh.zapara.data.campus.Node
import ru.bgtu_voenmeh.zapara.data.campus.Route

object RoutePresentationBuilder {
    fun build(route: Route, from: Node?, to: Node?, rasters: Map<FloorKey, RasterSize>): RoutePresentation {
        val steps = ArrayList<RouteStep>()
        val segments = ArrayList<RouteSegment>()
        val problems = linkedSetOf<RouteProblem>()
        val endpoints = listOfNotNull(
            endpoint(from, RouteMarkerKind.Start, rasters, problems),
            endpoint(to, RouteMarkerKind.Destination, rasters, problems)
        )
        val arrived = route.legs.isEmpty() && route.seconds == 0 && from != null && to != null &&
            from.id == to.id && endpoints.size == 2
        if (route.legs.isEmpty() && !arrived) problems.add(RouteProblem.MissingCoordinates)

        route.legs.forEachIndexed { legIndex, leg ->
            val kind = when (leg.kind) {
                "walk" -> RoutePartKind.Walk
                "stair_up" -> RoutePartKind.StairUp
                "stair_down" -> RoutePartKind.StairDown
                "building_link" -> RoutePartKind.BuildingLink
                else -> null
            }
            val source = FloorKey(leg.building, leg.floor)
            val target = if (kind == RoutePartKind.Walk) source
                else FloorKey(leg.toBuilding ?: leg.building, leg.toFloor ?: leg.floor)
            val localProblems = linkedSetOf<RouteProblem>()
            if (source !in rasters || target !in rasters) localProblems.add(RouteProblem.MissingMap)
            when {
                leg.points.isEmpty() -> localProblems.add(RouteProblem.MissingCoordinates)
                leg.points.any { !valid(it) } -> localProblems.add(RouteProblem.InvalidGeometry)
            }
            if (kind == null) {
                problems.addAll(localProblems)
                problems.add(RouteProblem.UnsupportedLeg)
                return@forEachIndexed
            }
            // A missing transition destination cannot establish ownership on a second plan.
            val missingTarget = kind != RoutePartKind.Walk && (leg.toBuilding == null || leg.toFloor == null)
            if (missingTarget) localProblems.add(RouteProblem.MissingCoordinates)
            val points = if (RouteProblem.InvalidGeometry in localProblems ||
                RouteProblem.MissingCoordinates in localProblems) emptyList() else leg.points.toList()
            val segmentIndices = if (kind == RoutePartKind.Walk ||
                (kind == RoutePartKind.BuildingLink && source == target && !missingTarget)) {
                val segmentIndex = segments.size
                segments.add(RouteSegment(legIndex, source, kind, points, localProblems.toSet()))
                listOf(segmentIndex)
            } else emptyList()
            val markers = transitionMarkers(kind, source, target, points)
            val previousLeg = route.legs.getOrNull(legIndex - 1)
            val continuesWalk = kind == RoutePartKind.Walk && previousLeg?.kind == "walk" &&
                previousLeg.building == source.building && previousLeg.floor == source.floor
            if (continuesWalk) {
                val previous = steps.last()
                steps[steps.lastIndex] = previous.copy(
                    segmentIndices = previous.segmentIndices + segmentIndices,
                    problems = previous.problems + localProblems
                )
            } else {
                steps.add(RouteStep(legIndex, kind, source, target, segmentIndices, markers, localProblems.toSet()))
            }
            problems.addAll(localProblems)
        }
        return RoutePresentation(steps.toList(), segments.toList(), endpoints, arrived, problems.toSet())
    }

    private fun valid(point: GraphPoint): Boolean =
        point.x.isFinite() && point.y.isFinite() && point.x in 0.0..1.0 && point.y in 0.0..1.0

    private fun endpoint(node: Node?, kind: RouteMarkerKind, rasters: Map<FloorKey, RasterSize>,
        problems: MutableSet<RouteProblem>): RouteMarker? {
        if (node == null) {
            problems.add(RouteProblem.MissingCoordinates)
            return null
        }
        val floor = FloorKey(node.building, node.floor)
        if (floor !in rasters) problems.add(RouteProblem.MissingMap)
        val point = GraphPoint(node.x, node.y)
        if (!valid(point)) {
            problems.add(RouteProblem.InvalidGeometry)
            return null
        }
        return RouteMarker(kind, floor, point)
    }

    private fun transitionMarkers(kind: RoutePartKind, from: FloorKey, to: FloorKey,
        points: List<GraphPoint>): List<RouteMarker> {
        if (points.isEmpty()) return emptyList()
        val (departure, arrival) = when (kind) {
            RoutePartKind.Walk -> return emptyList()
            RoutePartKind.StairUp, RoutePartKind.StairDown ->
                RouteMarkerKind.StairDeparture to RouteMarkerKind.StairArrival
            RoutePartKind.BuildingLink -> RouteMarkerKind.LinkDeparture to RouteMarkerKind.LinkArrival
        }
        return listOf(RouteMarker(departure, from, points.first(), to),
            RouteMarker(arrival, to, points.last(), from))
    }
}
