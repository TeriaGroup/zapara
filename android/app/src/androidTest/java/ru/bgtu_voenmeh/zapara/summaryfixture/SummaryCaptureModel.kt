package ru.bgtu_voenmeh.zapara

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryComposer
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryUiState

/** Pure fixture input and literal oracle; aggregation belongs only to SummaryComposer. */
internal object SummaryCaptureModel {
    val lessons = listOf(
        Lesson(dayOfWeek = 1, parity = 1, typeRaw = "лек", subjectRaw = "Математика",
            teacherRaw = "Иванов", classroomRaw = "101;"),
        Lesson(dayOfWeek = 1, parity = 2, typeRaw = "лек", subjectRaw = "Математика",
            teacherRaw = "Иванов", classroomRaw = "102;"),
        Lesson(dayOfWeek = 1, parity = 0, typeRaw = "лек", subjectRaw = "Математика",
            teacherRaw = "Иванов", classroomRaw = "101;"),
        Lesson(dayOfWeek = 2, parity = 1, typeRaw = "лек", subjectRaw = "Математика",
            teacherRaw = "Иванов", roomRaw = "", classroomRaw = "—;", buildingRaw = "ГК")
    )

    val cases = listOf(
        SummaryCaptureCase("odd", 0, 3, listOf(2, 1, 0, 0, 0, 0), listOf("101 ГК" to 2)),
        SummaryCaptureCase("even", 1, 2, listOf(2, 0, 0, 0, 0, 0), listOf("101 ГК" to 1, "102 ГК" to 1)),
        SummaryCaptureCase("both", 2, 4, listOf(3, 1, 0, 0, 0, 0), listOf("101 ГК" to 2, "102 ГК" to 1))
    )

    fun state(segment: Int, copy: UiCopy): SummaryUiState {
        require(segment in 0..2) { "Unknown summary segment: $segment" }
        return SummaryUiState(true, true, segment,
            SummaryComposer.tiles(segment, lessons, { _, _ -> "" }, copy))
    }
}

internal data class SummaryCaptureCase(
    val id: String,
    val segment: Int,
    val total: Int,
    val days: List<Int>,
    val rooms: List<Pair<String, Int>>
) {
    val substates: List<String> get() = listOf("top", "day") +
        days.indices.map { "day-${it + 1}" } + listOf("type", "subject", "teacher", "room") +
        rooms.indices.map { "room-$it" } + "bottom"
}

internal fun summaryRootWindowBounds(x: Float, y: Float, width: Float, height: Float): SummaryBounds =
    SummaryBounds(x, y, x + width, y + height)

/** Window-coordinate geometry, supplied only by the settled runtime observer. */
internal data class SummaryBounds(val left: Float, val top: Float, val right: Float, val bottom: Float) {
    init { require(listOf(left, top, right, bottom).all { it.isFinite() } && right > left && bottom > top) }
    fun contains(row: SummaryBounds) = row.left >= left && row.top >= top && row.right <= right && row.bottom <= bottom
    fun crossesVerticalBoundary(box: SummaryBounds) = box.left >= left && box.right <= right &&
        box.bottom > top && box.top < bottom && (box.top < top || box.bottom > bottom)
}

internal data class SummaryScroll(val value: Float, val maximum: Float, val canScrollForward: Boolean) {
    init { require(value.isFinite() && maximum.isFinite() && value >= 0f && maximum >= value) }
}

/** Last-row visibility is not an endpoint. Each step must advance the actual scroll range. */
internal fun driveSummaryBottom(maxSteps: Int = 32, read: () -> SummaryScroll, advance: () -> Unit): SummaryScroll {
    require(maxSteps > 0)
    var previous = read()
    repeat(maxSteps) {
        if (!previous.canScrollForward) return previous
        advance()
        val current = read()
        check(!current.canScrollForward || current.value > previous.value) { "Summary bottom unreachable: no forward progress" }
        previous = current
    }
    check(!previous.canScrollForward) { "Summary bottom unreachable: exhausted $maxSteps scroll steps" }
    return previous
}

internal data class SummaryFrameKey(val filter: String, val theme: String, val scale: String,
    val configuration: String, val build: String, val frame: String, val viewport: SummaryBounds)
internal data class SummaryObservation(val obligation: String, val key: SummaryFrameKey,
    val bounds: List<SummaryBounds>, val asserted: Boolean)

/** Per-run references, not accepted coverage. No static row-fit assumptions or PNG quota. */
internal class SummaryFrameLedger {
    private val credited = mutableSetOf<String>()
    val pending: List<String> get() = SummaryCaptureModel.cases.flatMap { c ->
        c.substates.map { "${c.id}-$it" }
    }.filterNot { it in credited }

    fun credit(key: SummaryFrameKey, observations: List<SummaryObservation>): List<SummaryObservation> {
        val expected = requireNotNull(SummaryCaptureModel.cases.singleOrNull { it.id == key.filter })
        require(key.theme in listOf("dark", "light") && key.scale in listOf("1.0", "1.5", "2.0"))
        require(key.configuration in listOf("phone360-portrait", "phone360-landscape", "tablet600-portrait", "tablet600-landscape"))
        require(key.build.isNotBlank() && key.frame.isNotBlank())
        require(observations.isNotEmpty()) { "Empty summary frame group" }
        require(observations.map { it.obligation }.distinct().size == observations.size) { "Duplicate obligation in frame" }
        observations.forEach {
            require(it.key == key && it.obligation in expected.substates) { "Unknown/mismatched summary observation" }
            require(it.bounds.isNotEmpty()) { "Runtime bounds required" }
            require("${key.filter}-${it.obligation}" !in credited) { "Obligation already credited" }
        }
        val eligible = observations.filter { it.asserted && it.bounds.all(key.viewport::contains) }
        require(eligible.isNotEmpty()) { "No fully visible asserted obligations" }
        credited += eligible.map { "${key.filter}-${it.obligation}" }
        return eligible
    }
}
