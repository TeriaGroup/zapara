package ru.bgtu_voenmeh.zapara

import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.ComposeTestRule
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.SemanticsActions
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import ru.bgtu_voenmeh.zapara.data.Parity

/** Drives obligations; fully observed rows can reference one current frame, never a hypothetical PNG. */
internal class SummaryCaptureActions(private val rule: ComposeTestRule) {
    private val ledger = SummaryFrameLedger()
    private lateinit var current: SummaryCaptureCase
    private var obligation = ""

    fun observeFrame(key: SummaryFrameKey): JSONObject {
        check(key.filter == current.id)
        val checks = SummaryCaptureAssertions(rule)
        val settledScroll = checks.scrollState()
        val settledViewport = checks.viewport()
        check(checks.rootViewport() == key.viewport) { "Summary root moved before observation" }
        val required = requireNotNull(checks.observe(current, obligation, key)) { "Summary target not fully visible: $obligation" }
        val rows = current.substates.filter { it.startsWith("day-") || it.startsWith("room-") }
            .filter { it != obligation && "${current.id}-$it" in ledger.pending }
            .mapNotNull { checks.observe(current, it, key) }
        check(checks.scrollState() == settledScroll && checks.viewport() == settledViewport &&
            checks.rootViewport() == key.viewport) { "Summary frame moved during observation" }
        val grouped = ledger.credit(key, listOf(required) + rows)
        fun bounds(b: SummaryBounds) = JSONObject().put("left", b.left).put("top", b.top).put("right", b.right).put("bottom", b.bottom)
        val scroll = checks.scrollState()
        return JSONObject().put("mappingVersion", 2).put("frame", key.frame).put("filter", key.filter)
            .put("configuration", key.configuration).put("viewport", bounds(key.viewport)).put("listViewport", bounds(checks.viewport()))
            .put("scroll", JSONObject().put("value", scroll.value).put("maximum", scroll.maximum).put("canScrollForward", scroll.canScrollForward))
            .put("obligations", JSONArray().apply { grouped.forEach { row -> put(JSONObject()
                .put("id", "${key.filter}-${row.obligation}").put("asserted", row.asserted)
                .put("bounds", JSONArray().apply { row.bounds.forEach { put(bounds(it)) } })) } })
            .put("pending", JSONArray(ledger.pending)).put("accepted", false)
    }

    fun capture(fixture: UiMapsCaptureFixtures, shot: (String, () -> Unit) -> Unit) {
        val checks = SummaryCaptureAssertions(rule)
        SummaryCaptureModel.cases.forEach { expected ->
            current = expected
            fun frame(state: String, action: () -> Unit) {
                if ("${expected.id}-$state" !in ledger.pending) return
                obligation = state
                shot("${expected.id}-$state", action)
            }
            fun scroll(index: Int) = rule.onNodeWithTag("Summary.List").performScrollToIndex(index)
            frame("top") {
                rule.onNodeWithTag("Summary.Segment.${expected.segment}").performClick().assertIsSelected()
                scroll(0)
                rule.runOnIdle { assertEquals(expected.segment, fixture.summary.segment) }
                rule.onNodeWithTag("Top.Title").assertTextEquals("Сводка").assertIsDisplayed()
                rule.onNodeWithTag("Summary.Total").assertTextEquals(expected.total.toString()).assertIsDisplayed()
            }
            frame("day") {
                scroll(1)
                rule.onNodeWithTag("Summary.ByDay").assertIsDisplayed()
                checks.rowCount("Summary.ByDay", "Summary.Day.", expected.days.size)
                rule.onNode(hasText("По дням") and hasAnyAncestor(hasTestTag("Summary.ByDay")), true).assertIsDisplayed()
            }
            expected.days.forEachIndexed { index, count ->
                val day = index + 1
                frame("day-$day") {
                    scroll(1)
                    rule.onNodeWithTag("Summary.Day.$day", true).performScrollTo()
                    checks.taggedRow("Summary.ByDay", "Summary.Day.$day", Parity.dayNumberToTitle(day), count)
                }
            }
            listOf(Triple("type", "По типам", "лекция"), Triple("subject", "По предметам", "Математика"),
                Triple("teacher", "По преподавателям", "Иванов")).forEachIndexed { index, (state, title, name) ->
                frame(state) {
                    scroll(index + 2)
                    checks.singleCountCard(title, name, expected.total)
                }
            }
            frame("room") {
                scroll(5)
                rule.onNodeWithTag("Summary.ByRoom").assertIsDisplayed()
                rule.onNode(hasText("По аудиториям") and hasAnyAncestor(hasTestTag("Summary.ByRoom")), true).assertIsDisplayed()
            }
            expected.rooms.forEachIndexed { index, (name, count) ->
                frame("room-$index") {
                    scroll(5)
                    rule.onNodeWithTag("Summary.Room.$index", true).performScrollTo()
                    checks.taggedRow("Summary.ByRoom", "Summary.Room.$index", name, count)
                }
            }
            frame("bottom") {
                scroll(5)
                val last = expected.rooms.lastIndex
                rule.onNodeWithTag("Summary.Room.$last", true).performScrollTo()
                driveSummaryBottom(read = checks::scrollState, advance = {
                    val distance = checks.viewport().let { (it.bottom - it.top) / 2f }
                    rule.onNodeWithTag("Summary.List").performSemanticsAction(SemanticsActions.ScrollBy) { scrollBy ->
                        check(scrollBy(0f, distance)) { "Summary bottom unreachable: ScrollBy rejected" }
                    }
                    rule.waitForIdle()
                })
                checks.taggedRow("Summary.ByRoom", "Summary.Room.$last", expected.rooms.last().first, expected.rooms.last().second)
                checks.rowCount("Summary.ByRoom", "Summary.Room.", expected.rooms.size)
                val range = rule.onNodeWithTag("Summary.List").fetchSemanticsNode().config[SemanticsProperties.VerticalScrollAxisRange]
                rule.runOnIdle { assertEquals("Summary.List reached bottom", range.maxValue(), range.value(), 0.001f) }
                rule.onNode(hasText("Аудитории не указаны") and hasAnyAncestor(hasTestTag("Summary.ByRoom")), true).assertDoesNotExist()
            }
            check(ledger.pending.none { it.startsWith("${expected.id}-") }) { "Unobserved summary obligations: ${ledger.pending}" }
        }
    }
}
