package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.translate
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import kotlin.math.roundToInt

internal object RouteOverlayStyle {
    val stroke = 3.dp
    val activeStroke = 4.dp
    val outline = 6.dp
    val arrowHead = 8.dp
    val arrowSpacing = 48.dp
    val marker = 24.dp
    val padding = 4.dp
    val numberPadding = 2.dp
    val connector = 1.dp
}

/** Layout pixels only. Disconnected strokes and FloorKeys never share a path. */
internal fun routeOverlayPoints(segment: RouteSegment, floor: FloorKey, fitted: FittedImage): List<PathPx> {
    if (segment.floor != floor || segment.kind !in setOf(RoutePartKind.Walk, RoutePartKind.BuildingLink) ||
        segment.problems.any { it != RouteProblem.MissingMap } || segment.points.any {
            !it.x.isFinite() || !it.y.isFinite() || it.x !in 0.0..1.0 || it.y !in 0.0..1.0
        }) return emptyList()
    return segment.points.map { PathPx(fitted.originX + it.x.toFloat() * fitted.drawnW,
        fitted.originY + it.y.toFloat() * fitted.drawnH) }
}

@Composable
fun RouteMapOverlay(presentation: RoutePresentation, floor: FloorKey, activeStepId: Int?, fitted: FittedImage,
    modifier: Modifier = Modifier, visibleBounds: HighlightGeometry.ChipBox? = null) {
    val c = Zapara.colors
    val active = presentation.steps.firstOrNull { it.id == activeStepId }
    val strokes = remember(presentation, floor, fitted) {
        presentation.segments.mapIndexed { index, segment -> index to routeOverlayPoints(segment, floor, fitted) }
    }
    val groups = remember(presentation, floor) { routeMarkerGroups(presentation, floor) }
    var links by remember(presentation, floor, fitted) { mutableStateOf(emptyList<Pair<PathPx, HighlightGeometry.ChipBox>>()) }
    Box(modifier.fillMaxSize().testTag("Maps.RouteOverlay")) {
        Canvas(Modifier.fillMaxSize()) {
            // Leaders are below route ink, so even a crossing cannot mask a short route.
            links.forEach { (anchor, box) ->
                drawLine(c.text2, Offset(anchor.x, anchor.y),
                    Offset(anchor.x.coerceIn(box.x, box.x + box.w), anchor.y.coerceIn(box.y, box.y + box.h)),
                    strokeWidth = RouteOverlayStyle.connector.toPx())
            }
            strokes.sortedBy { (index, _) -> index in active?.segmentIndices.orEmpty() }.forEach { (index, points) ->
                if (points.size < 2) return@forEach
                val path = Path().apply {
                    moveTo(points.first().x, points.first().y)
                    points.drop(1).forEach { lineTo(it.x, it.y) }
                }
                val selected = index in active?.segmentIndices.orEmpty()
                drawPath(path, c.onMapInk, style = Stroke(RouteOverlayStyle.outline.toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round))
                drawPath(path, c.mapInk, style = Stroke(
                    (if (selected) RouteOverlayStyle.activeStroke else RouteOverlayStyle.stroke).toPx(), cap = StrokeCap.Round, join = StrokeJoin.Round))
                RouteGeometry.arrows(points, RouteOverlayStyle.arrowSpacing.toPx()).forEach { arrow ->
                    val head = RouteOverlayStyle.arrowHead.toPx()
                    val tip = Offset(arrow.tip.x, arrow.tip.y)
                    val back = tip - Offset(arrow.unitX, arrow.unitY) * head
                    val wing = Offset(-arrow.unitY, arrow.unitX) * (head / 2)
                    val chevron = Path().apply {
                        moveTo((back + wing).x, (back + wing).y); lineTo(tip.x, tip.y)
                        lineTo((back - wing).x, (back - wing).y)
                    }
                    drawPath(chevron, c.onMapInk, style = Stroke(RouteOverlayStyle.outline.toPx(), cap = StrokeCap.Round))
                    drawPath(chevron, c.mapInk, style = Stroke(RouteOverlayStyle.stroke.toPx(), cap = StrokeCap.Round))
                }
            }
        }
        groups.forEach { group ->
            val marker = group.first()
            Canvas(Modifier.fillMaxSize()) {
                val markerSize = RouteOverlayStyle.marker.toPx()
                val x = fitted.originX + marker.point.x.toFloat() * fitted.drawnW
                val y = fitted.originY + marker.point.y.toFloat() * fitted.drawnH
                // Keep the symbol centre on the graph coordinate, independently of label placement.
                translate(x - markerSize / 2, y - markerSize / 2) {
                    val size = Size(markerSize, markerSize)
                    drawRect(c.onMapInk, size = size)
                    val pad = RouteOverlayStyle.padding.toPx()
                    val stroke = Stroke(RouteOverlayStyle.stroke.toPx(), cap = StrokeCap.Round)
                    when (marker.kind) {
                        RouteMarkerKind.Start -> drawCircle(c.mapInk, (size.minDimension - pad * 2) / 2,
                            center = Offset(markerSize / 2, markerSize / 2), style = stroke)
                        RouteMarkerKind.Destination -> drawRect(c.mapInk, Offset(pad, pad), Size(size.width - 2 * pad, size.height - 2 * pad), style = stroke)
                        RouteMarkerKind.StairDeparture, RouteMarkerKind.StairArrival -> {
                            val path = Path().apply {
                                moveTo(pad, size.height - pad); lineTo(size.width / 2, size.height - pad)
                                lineTo(size.width / 2, size.height / 2); lineTo(size.width - pad, size.height / 2)
                                lineTo(size.width - pad, pad)
                            }
                            drawPath(path, c.mapInk, style = stroke)
                        }
                        RouteMarkerKind.LinkDeparture, RouteMarkerKind.LinkArrival -> {
                            val path = Path().apply {
                                moveTo(pad, size.height / 2); lineTo(size.width - pad, size.height / 2)
                                moveTo(size.width / 2, pad); lineTo(size.width - pad, size.height / 2); lineTo(size.width / 2, size.height - pad)
                            }
                            drawPath(path, c.mapInk, style = stroke)
                        }
                    }
                }
            }
        }
        Layout(modifier = Modifier.fillMaxSize(), content = {
            groups.forEachIndexed { index, group ->
                val label = "${index + 1}. ${routeMarkerText(group)}"
                Text(label, style = Zapara.typography.caption, color = c.text1,
                    modifier = Modifier.testTag("Maps.Marker.${group.first().kind}")
                        .background(c.card, RoundedCornerShape(Zapara.radii.chip)).padding(RouteOverlayStyle.padding))
                Text("${index + 1}", style = compactMapNumberStyle(), color = c.text1,
                    modifier = Modifier.testTag("Maps.MarkerNumber.$index")
                        .clearAndSetSemantics { contentDescription = label }
                        .background(c.card, RoundedCornerShape(Zapara.radii.chip)).padding(RouteOverlayStyle.numberPadding))
            }
        }) { measurables, constraints ->
            val measured = measurables.map { child -> child.measure(Constraints(maxWidth =
                maxOf(constraints.maxWidth / 2, child.minIntrinsicWidth(Constraints.Infinity)).coerceAtMost(constraints.maxWidth))) }
            val gap = RouteOverlayStyle.padding.toPx()
            val markerSize = RouteOverlayStyle.marker.toPx()
            val occupied = RouteLabelPlacement.routeBounds(strokes.map { it.second }, RouteOverlayStyle.arrowHead.toPx()).toMutableList()
            occupied += groups.map { group ->
                val p = PathGeometry.onLayout(group.first().point.x.toFloat(), group.first().point.y.toFloat(), fitted)
                HighlightGeometry.ChipBox(p.x - markerSize / 2, p.y - markerSize / 2, markerSize, markerSize)
            }
            val viewport = visibleBounds ?: HighlightGeometry.ChipBox(0f, 0f, constraints.maxWidth.toFloat(), constraints.maxHeight.toFloat())
            val raster = HighlightGeometry.ChipBox(fitted.originX, fitted.originY, fitted.drawnW, fitted.drawnH)
            val positions = groups.mapIndexed { index, group ->
                val marker = group.first()
                val x = fitted.originX + marker.point.x.toFloat() * fitted.drawnW
                val y = fitted.originY + marker.point.y.toFloat() * fitted.drawnH + markerSize / 2 + gap
                listOf(index * 2, index * 2 + 1).firstNotNullOfOrNull { childIndex ->
                    val child = measured[childIndex]
                    if (measurables[childIndex].minIntrinsicWidth(Constraints.Infinity) > child.width) null
                    else {
                        // Full captions stay off the plan; compact numbers may sit near markers.
                        val blocked = if (childIndex % 2 == 0) occupied + raster else occupied
                        RouteLabelPlacement.place(x, y, child.width.toFloat(), child.height.toFloat(), blocked, viewport, gap)
                            ?.let { childIndex to it }
                    }
                }?.also { occupied += it.second }
            }
            links = positions.mapIndexedNotNull { index, placed -> placed?.let {
                PathGeometry.onLayout(groups[index].first().point.x.toFloat(), groups[index].first().point.y.toFloat(), fitted) to it.second
            } }
            layout(constraints.maxWidth, constraints.maxHeight) {
                positions.filterNotNull().forEach { (index, box) -> measured[index].place(box.x.roundToInt(), box.y.roundToInt()) }
            }
        }
    }
}

@Composable
private fun compactMapNumberStyle(): TextStyle {
    val scale = LocalDensity.current.fontScale.coerceAtLeast(0.01f)
    // Keep on-map digits near the 24dp marker; the legend still uses scaled captions.
    return Zapara.typography.caption.copy(fontSize = (11f / scale).sp, lineHeight = (12f / scale).sp)
}

@Composable
internal fun markerText(marker: RouteMarker): String = when (marker.kind) {
    RouteMarkerKind.Start -> stringResource(R.string.maps_start_marker)
    RouteMarkerKind.Destination -> stringResource(R.string.maps_destination_marker)
    RouteMarkerKind.StairDeparture, RouteMarkerKind.LinkDeparture -> {
        val target = marker.target ?: marker.floor
        stringResource(R.string.maps_transition_departure, target.building, target.floor)
    }
    RouteMarkerKind.StairArrival, RouteMarkerKind.LinkArrival ->
        stringResource(R.string.maps_transition_arrival, marker.floor.building, marker.floor.floor)
}

@Composable
internal fun routeMarkerText(group: List<RouteMarker>): String =
    if (group.any { it.kind == RouteMarkerKind.Start } && group.any { it.kind == RouteMarkerKind.Destination })
        stringResource(R.string.maps_start_destination_marker)
    else group.map { markerText(it) }.distinct().joinToString("; ")
