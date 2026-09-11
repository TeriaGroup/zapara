package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.CoordsRect
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.campus.CampusGraph
import ru.bgtu_voenmeh.zapara.data.campus.CampusRouter
import ru.bgtu_voenmeh.zapara.data.campus.Leg
import ru.bgtu_voenmeh.zapara.data.campus.Node
import ru.bgtu_voenmeh.zapara.data.campus.Route
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.temporal.ChronoUnit

enum class MapMode { None, NextLesson, Lesson, Manual }

data class ShownPlan(val building: String, val floor: Int, val roomRaw: String)

data class HighlightUi(val rect: CoordsRect, val label: String)

data class HighlightPx(val left: Float, val top: Float, val right: Float, val bottom: Float)

data class FittedImage(
    val originX: Float,
    val originY: Float,
    val drawnW: Float,
    val drawnH: Float
)

enum class BoxChildOrigin { TopStart, Center }

object HighlightGeometry {
    fun fit(layoutW: Float, layoutH: Float, imageW: Float, imageH: Float): FittedImage {
        if (imageW <= 0f || imageH <= 0f || layoutW <= 0f || layoutH <= 0f) {
            return FittedImage(0f, 0f, layoutW.coerceAtLeast(0f), layoutH.coerceAtLeast(0f))
        }
        val scale = minOf(layoutW / imageW, layoutH / imageH)
        val drawnW = imageW * scale
        val drawnH = imageH * scale
        return FittedImage((layoutW - drawnW) / 2f, (layoutH - drawnH) / 2f, drawnW, drawnH)
    }

    fun onLayout(rect: CoordsRect, fitted: FittedImage): HighlightPx {
        val left = fitted.originX + rect.x.toFloat() * fitted.drawnW
        val top = fitted.originY + rect.y.toFloat() * fitted.drawnH
        return HighlightPx(left, top, left + rect.w.toFloat() * fitted.drawnW, top + rect.h.toFloat() * fitted.drawnH)
    }

    fun afterLayer(px: HighlightPx, scale: Float, offsetX: Float, offsetY: Float, pivotX: Float, pivotY: Float): HighlightPx {
        fun map(x: Float, y: Float): Pair<Float, Float> =
            (pivotX + (x - pivotX) * scale + offsetX) to (pivotY + (y - pivotY) * scale + offsetY)
        val (left, top) = map(px.left, px.top)
        val (right, bottom) = map(px.right, px.bottom)
        return HighlightPx(left, top, right, bottom)
    }

    fun mapped(
        rect: CoordsRect,
        layoutW: Float,
        layoutH: Float,
        imageW: Float,
        imageH: Float,
        scale: Float,
        offsetX: Float,
        offsetY: Float
    ): HighlightPx {
        val local = onLayout(rect, fit(layoutW, layoutH, imageW, imageH))
        return afterLayer(local, scale, offsetX, offsetY, layoutW / 2f, layoutH / 2f)
    }

    /** Inverse of [mapped]: layout tap → normalized plan coordinates, or null outside the raster. */
    fun fromLayout(
        tapX: Float,
        tapY: Float,
        layoutW: Float,
        layoutH: Float,
        imageW: Float,
        imageH: Float,
        scale: Float,
        offsetX: Float,
        offsetY: Float
    ): Pair<Float, Float>? {
        if (scale == 0f || layoutW <= 0f || layoutH <= 0f) return null
        val pivotX = layoutW / 2f
        val pivotY = layoutH / 2f
        val localX = pivotX + (tapX - offsetX - pivotX) / scale
        val localY = pivotY + (tapY - offsetY - pivotY) / scale
        val fitted = fit(layoutW, layoutH, imageW, imageH)
        if (fitted.drawnW <= 0f || fitted.drawnH <= 0f) return null
        val nx = (localX - fitted.originX) / fitted.drawnW
        val ny = (localY - fitted.originY) / fitted.drawnH
        if (nx < 0f || ny < 0f || nx > 1f || ny > 1f) return null
        return nx to ny
    }

    /** Offset to apply so a wrap-content chip sits above [mapped] when the child origin is TopStart. */
    fun chipOffset(mapped: HighlightPx, gapPx: Float): Pair<Float, Float> =
        mapped.left to (mapped.top - gapPx)

    data class ChipBox(val x: Float, val y: Float, val w: Float, val h: Float)

    fun overlaps(a: ChipBox, b: ChipBox, margin: Float): Boolean =
        a.x < b.x + b.w + margin && a.x + a.w + margin > b.x &&
            a.y < b.y + b.h + margin && a.y + a.h + margin > b.y

    fun dodge(
        x: Float,
        y: Float,
        w: Float,
        h: Float,
        occupied: List<ChipBox>,
        layoutW: Float,
        layoutH: Float,
        margin: Float
    ): Pair<Float, Float> {
        RouteLabelPlacement.place(x, y, w, h, occupied, ChipBox(0f, 0f, layoutW, layoutH), margin)?.let {
            return it.x to it.y
        }
        var left = x
        var top = y
        fun box() = ChipBox(left, top, w, h)
        for (other in occupied) {
            if (!overlaps(box(), other, margin)) continue
            val toLeft = other.x - w - margin
            left = if (toLeft >= 0f) toLeft else other.x + other.w + margin
            if (overlaps(box(), other, margin)) {
                top = (other.y - h - margin).coerceAtLeast(0f)
            }
            if (overlaps(box(), other, margin)) {
                top = other.y + other.h + margin
            }
        }
        val maxX = (layoutW - w).coerceAtLeast(0f)
        val maxY = (layoutH - h).coerceAtLeast(0f)
        return left.coerceIn(0f, maxX) to top.coerceIn(0f, maxY)
    }

    /**
     * Visual top-left after Compose [Modifier.offset] on a wrap-content child.
     * Center origin is the parent contentAlignment=Center placement; TopStart matches Canvas fillMaxSize.
     */
    fun offsetVisual(
        offsetX: Float,
        offsetY: Float,
        parentW: Float,
        parentH: Float,
        childW: Float,
        childH: Float,
        origin: BoxChildOrigin
    ): Pair<Float, Float> {
        val baseX = if (origin == BoxChildOrigin.Center) (parentW - childW) / 2f else 0f
        val baseY = if (origin == BoxChildOrigin.Center) (parentH - childH) / 2f else 0f
        return (baseX + offsetX) to (baseY + offsetY)
    }
}

object MapsComposer {
    fun stairMarkers(route: Route?, building: String, floor: Int): List<StairMarkerUi> {
        if (route == null) return emptyList()
        val markers = ArrayList<StairMarkerUi>()
        fun add(x: Double, y: Double, label: String, departure: Boolean) {
            val marker = StairMarkerUi(x, y, label, departure)
            val existing = markers.indexOfFirst { kotlin.math.abs(it.x - x) < .00001 && kotlin.math.abs(it.y - y) < .00001 }
            if (existing < 0) markers.add(marker)
            else if (departure && !markers[existing].isDeparture) markers[existing] = marker
        }
        for (leg in route.legs) {
            if ((leg.kind != "stair_up" && leg.kind != "stair_down") || leg.points.isEmpty()) continue
            val arrow = if (leg.kind == "stair_up") "↑" else "↓"
            if (leg.building == building && leg.floor == floor && leg.toFloor != null) {
                val p = leg.points.first()
                add(p.x, p.y, "$arrow ${leg.toFloor}", true)
            }
            if ((leg.toBuilding ?: leg.building) == building && leg.toFloor == floor) {
                val p = leg.points.last()
                add(p.x, p.y, "$arrow \u0441 ${leg.floor}", false)
            }
        }
        return markers
    }

    fun floors(building: String): List<Int> = if (building == "УЛК") (1..5).toList() else (1..4).toList()

    fun places(graph: CampusGraph): List<RoutePlaceUi> {
        val out = ArrayList<RoutePlaceUi>()
        for (node in graph.nodes) {
            if (node.kind != "entrance" && node.kind != "room") continue
            val label = placeLabel(node)
            val hint = "${node.building}, ${node.floor}"
            val hay = listOfNotNull(label, node.id, node.room, node.label, node.building).joinToString(" ").lowercase()
            out.add(RoutePlaceUi(node.id, label, hint, node.kind, node.building, node.floor, hay))
        }
        out.sortWith(compareBy({ it.kind != "entrance" }, { it.building }, { it.floor }, { it.label }))
        return out
    }

    fun filterPlaces(places: List<RoutePlaceUi>, query: String): List<RoutePlaceUi> {
        val q = query.trim().lowercase()
        if (q.isEmpty()) return places
        return places.filter { it.hay.contains(q) }
    }

    fun pickerItems(
        places: List<RoutePlaceUi>,
        query: String,
        building: String?,
        floor: Int?
    ): List<RoutePlaceUi> {
        if (query.trim().isNotEmpty()) return filterPlaces(places, query)
        return places.filter { place ->
            when {
                place.kind == "entrance" -> building == null || place.building == building
                building != null && place.building != building -> false
                floor != null && place.floor != floor -> false
                else -> true
            }
        }
    }

    fun placeLabel(node: Node): String = when {
        node.kind == "entrance" -> node.label?.ifBlank { null } ?: node.id
        !node.room.isNullOrBlank() -> "${node.room} · ${node.building}"
        !node.label.isNullOrBlank() -> node.label!!
        else -> node.id
    }

    fun durationLabel(seconds: Double, copy: UiCopy): String? {
        if (seconds <= 0) return null
        if (seconds < 60) return copy.get("maps_route_under_minute")
        return copy.get("maps_route_minutes", kotlin.math.round(seconds / 60.0).toInt())
    }

    fun floorRooms(
        graph: CampusGraph,
        building: String,
        floor: Int,
        coords: Map<String, ru.bgtu_voenmeh.zapara.data.CoordsRect>
    ): List<FloorRoom> {
        val shown = if (building == "ВЦ") "ГК" else building
        val out = ArrayList<FloorRoom>()
        for (node in graph.nodes) {
            if (node.kind != "room" || node.building != shown || node.floor != floor) continue
            val key = node.room?.trim()?.lowercase() ?: continue
            val rect = coords[key] ?: continue
            out.add(FloorRoom(node.id, node.room!!, rect))
        }
        return out
    }

    fun hitRoom(nx: Double, ny: Double, rooms: List<FloorRoom>): FloorRoom? {
        var best: FloorRoom? = null
        var bestArea = Double.POSITIVE_INFINITY
        for (room in rooms) {
            val r = room.rect
            if (nx < r.x || ny < r.y || nx > r.x + r.w || ny > r.y + r.h) continue
            val area = r.w * r.h
            if (area < bestArea) {
                best = room
                bestArea = area
            }
        }
        return best
    }

    fun preferredEntrance(graph: CampusGraph, building: String): Node? {
        val shown = if (building == "ВЦ") "ГК" else building
        val list = graph.nodes.filter { it.kind == "entrance" && it.building == shown }
        return list.firstOrNull { it.id.endsWith(".main") } ?: list.firstOrNull()
    }

    fun startFallbackMessage(chosenLabel: String?, copy: ru.bgtu_voenmeh.zapara.ui.UiCopy): String? {
        if (chosenLabel.isNullOrBlank()) return null
        return copy.get("maps_route_start_fallback", chosenLabel)
    }

    fun startFor(graph: CampusGraph, fromId: String?, toId: String?): Node? {
        val requested = fromId?.let { id -> graph.nodes.firstOrNull { it.id == id } }
        val dest = toId?.let { id -> graph.nodes.firstOrNull { it.id == id } } ?: return requested
        if (requested == null) return null
        val candidates = ArrayList<String>()
        candidates.add(requested.id)
        preferredEntrance(graph, dest.building)?.id?.let { candidates.add(it) }
        for (id in candidates.distinct()) {
            val result = CampusRouter.find(graph, id, dest.id)
            if (result.ok) return graph.nodes.firstOrNull { it.id == id }
        }
        return requested
    }

    fun shownPlan(building: String, floor: Int, roomRaw: String, node: ru.bgtu_voenmeh.zapara.data.campus.Node?): ShownPlan {
        if (node == null) {
            val shown = if (building == "ВЦ") "ГК" else building
            return ShownPlan(shown, floor, roomRaw)
        }
        val shown = if (building == "ВЦ") "ГК" else node.building
        return ShownPlan(shown, node.floor, node.room ?: roomRaw)
    }

    fun roomText(lesson: Lesson, copy: UiCopy): String = LessonFormat.roomLabel(lesson, copy)

    fun contextLine(now: LocalDateTime, lessonsToday: List<Lesson>, next: Pair<LocalDate, Lesson>?, copy: UiCopy): String {
        val going = lessonsToday.firstOrNull { lesson ->
            val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: return@firstOrNull false
            val end = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull() ?: return@firstOrNull false
            val t = now.toLocalTime()
            !t.isBefore(start) && t.isBefore(end)
        }
        if (going != null) return copy.get("map_ctx_now", roomText(going, copy), going.timeEnd)
        if (next == null) return copy.get("map_ctx_none")
        val (date, lesson) = next
        val room = roomText(lesson, copy)
        val today = now.toLocalDate()
        if (date == today) {
            val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull()
            val minutes = if (start != null) ChronoUnit.MINUTES.between(now.toLocalTime(), start).toInt().coerceAtLeast(1) else 0
            return copy.get("map_ctx_next_min", room, minutes)
        }
        if (date == today.plusDays(1)) return copy.get("map_ctx_next_tomorrow", lesson.timeStart, room)
        return copy.get("map_ctx_next_date", LessonFormat.monthDay(date), lesson.timeStart, room)
    }

    fun nextLesson(
        now: LocalDateTime,
        lessonsByDate: (LocalDate) -> List<Lesson>,
        horizonDays: Int = 14
    ): Pair<LocalDate, Lesson>? {
        val today = now.toLocalDate()
        val nowTime = now.toLocalTime()
        for (offset in 0..horizonDays) {
            val date = today.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val day = lessonsByDate(date)
            val lesson = if (offset == 0) {
                day.firstOrNull { runCatching { LocalTime.parse(it.timeStart) }.getOrNull()?.let { start -> start.isAfter(nowTime) } == true }
            } else {
                day.firstOrNull()
            }
            if (lesson != null) return date to lesson
        }
        return null
    }

    fun previousLessonToday(today: List<Lesson>, target: Lesson, now: LocalDateTime, targetDate: LocalDate): Lesson? {
        if (targetDate != now.toLocalDate()) return null
        val targetStart = runCatching { LocalTime.parse(target.timeStart) }.getOrNull() ?: return null
        val nowTime = now.toLocalTime()
        var prev: Lesson? = null
        for (lesson in today.sortedBy { runCatching { LocalTime.parse(it.timeStart) }.getOrNull() ?: LocalTime.MAX }) {
            val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: continue
            if (start.isBefore(targetStart) && !start.isAfter(nowTime)) prev = lesson
        }
        return prev
    }

    fun floorPathStrokes(route: Route?, building: String, floor: Int): List<List<Pair<Float, Float>>> {
        if (route == null) return emptyList()
        val strokes = ArrayList<List<Pair<Float, Float>>>()
        for (leg in route.legs) {
            if (!drawnOnFloor(leg, building, floor) || leg.points.isEmpty()) continue
            strokes.add(leg.points.map { it.x.toFloat() to it.y.toFloat() })
        }
        return strokes
    }

    private fun drawnOnFloor(leg: ru.bgtu_voenmeh.zapara.data.campus.Leg, building: String, floor: Int): Boolean =
        leg.floor == floor && leg.building == building &&
            (leg.kind == "walk" || (leg.kind == "building_link" && leg.toBuilding == building && leg.toFloor == floor))

    fun formatRouteSteps(route: Route, copy: UiCopy): List<RouteStepUi> {
        val items = ArrayList<RouteStepUi>(route.legs.size)
        var previous: Leg? = null
        for (leg in route.legs) {
            val continuesWalk = leg.kind == "walk" && previous?.kind == "walk" &&
                previous.building == leg.building && previous.floor == leg.floor
            previous = leg
            if (continuesWalk) continue

            val text = when (leg.kind) {
                "stair_down" -> copy.get("route_stair_down", leg.toFloor ?: leg.floor)
                "stair_up" -> copy.get("route_stair_up", leg.toFloor ?: leg.floor)
                "walk" -> copy.get("route_walk", leg.floor)
                "building_link" -> copy.get("route_link", leg.toBuilding ?: leg.building, leg.toFloor ?: leg.floor)
                else -> null
            }
            if (text != null) {
                items.add(RouteStepUi(text, leg.toBuilding ?: leg.building, leg.toFloor ?: leg.floor))
            }
        }
        return items
    }

    fun highlightRoom(building: String, floor: Int, dest: Node?, from: Node?): String? {
        val shown = if (building == "ВЦ") "ГК" else building
        fun hit(n: Node?): String? {
            if (n == null) return null
            val b = if (n.building == "ВЦ") "ГК" else n.building
            if (b != shown || n.floor != floor) return null
            return n.room?.takeIf { it.isNotBlank() }
        }
        return hit(dest) ?: hit(from)
    }

    fun visibleSteps(steps: List<RouteStepUi>, building: String, floor: Int, max: Int = 2): List<RouteStepUi> {
        if (steps.size <= max) return steps
        val idx = steps.indexOfFirst { it.building == building && it.floor == floor }.let { if (it < 0) 0 else it }
        val from = idx.coerceAtMost(steps.size - max).coerceAtLeast(0)
        return steps.subList(from, from + max)
    }
}
