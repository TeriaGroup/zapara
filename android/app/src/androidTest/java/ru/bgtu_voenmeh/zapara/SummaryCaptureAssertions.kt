@file:Suppress("INVISIBLE_MEMBER", "INVISIBLE_REFERENCE")

package ru.bgtu_voenmeh.zapara

import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.ComposeTestRule

/** Counts must belong to the exact card/row, never another visible summary tile. */
internal class SummaryCaptureAssertions(private val rule: ComposeTestRule) {
    fun bounds(node: SemanticsNode): SummaryBounds {
        val coordinates = node.layoutNode.innerCoordinator
        check(coordinates.isAttached)
        val p = coordinates.positionInWindow()
        return SummaryBounds(p.x, p.y, p.x + coordinates.size.width, p.y + coordinates.size.height)
    }

    fun rootViewport(): SummaryBounds {
        val node = rule.onNodeWithTag("Summary.List").fetchSemanticsNode()
        val root = generateSequence(node) { it.parent }.last()
        return rule.runOnIdle {
            val box = bounds(root)
            summaryRootWindowBounds(box.left, box.top, box.right - box.left, box.bottom - box.top)
        }
    }

    fun viewport(): SummaryBounds {
        val box = rule.onNodeWithTag("Summary.List").fetchSemanticsNode().boundsInWindow
        return SummaryBounds(box.left, box.top, box.right, box.bottom)
    }

    fun scrollState(): SummaryScroll {
        val range = rule.onNodeWithTag("Summary.List").fetchSemanticsNode().config[SemanticsProperties.VerticalScrollAxisRange]
        return rule.runOnIdle { SummaryScroll(range.value(), range.maxValue(), range.value() < range.maxValue()) }
    }

    fun observe(expected: SummaryCaptureCase, state: String, key: SummaryFrameKey): SummaryObservation? {
        val targets = when {
            state == "top" -> listOf(hasTestTag("Top.Title"), hasTestTag("Summary.Total"), hasTestTag("Summary.Segment.${expected.segment}"))
            state == "day" -> listOf(hasText("По дням") and hasAnyAncestor(hasTestTag("Summary.ByDay")))
            state == "room" -> listOf(hasText("По аудиториям") and hasAnyAncestor(hasTestTag("Summary.ByRoom")))
            state.startsWith("day-") -> listOf(hasTestTag("Summary.Day.${state.removePrefix("day-")}"))
            state.startsWith("room-") -> listOf(hasTestTag("Summary.Room.${state.removePrefix("room-")}"))
            state == "bottom" -> listOf(hasTestTag("Summary.Room.${expected.rooms.lastIndex}"))
            else -> {
                val (title, name) = when (state) {
                    "type" -> "По типам" to "лекция"
                    "subject" -> "По предметам" to "Математика"
                    "teacher" -> "По преподавателям" to "Иванов"
                    else -> error("Unknown summary obligation: $state")
                }
                listOf(hasAnyChild(hasText(title)) and hasAnyChild(hasText(name)))
            }
        }
        val nodes = targets.map { matcher ->
            rule.onAllNodes(matcher, true).fetchSemanticsNodes().singleOrNull() ?: return null
        }
        val boxes = rule.runOnIdle { nodes.map(::bounds) }
        val listViewport = viewport()
        val full = boxes.all(key.viewport::contains) && nodes.zip(boxes).all { (node, box) ->
            !insideList(node) || listViewport.contains(box)
        }
        if (!full) return null
        when {
            state.startsWith("day-") -> {
                val day = state.removePrefix("day-").toInt()
                taggedRow("Summary.ByDay", "Summary.Day.$day", ru.bgtu_voenmeh.zapara.data.Parity.dayNumberToTitle(day), expected.days[day - 1])
            }
            state.startsWith("room-") -> {
                val index = state.removePrefix("room-").toInt()
                taggedRow("Summary.ByRoom", "Summary.Room.$index", expected.rooms[index].first, expected.rooms[index].second)
            }
        }
        // Each text child of an eligible row/card must retain the strict painted/clipping guards.
        fun verify(node: SemanticsNode) {
            if (node.config.contains(androidx.compose.ui.semantics.SemanticsActions.GetTextLayoutResult))
                RenderedTextEvidence.check(node, key.scale.toFloat())
            node.children.forEach(::verify)
        }
        rule.runOnIdle { nodes.forEach(::verify) }
        return SummaryObservation(state, key, boxes, true)
    }

    fun insideList(node: SemanticsNode): Boolean = generateSequence(node.parent) { it.parent }.any {
        it.config.getOrElseNullable(SemanticsProperties.TestTag) { null } == "Summary.List"
    }

    fun rowCount(cardTag: String, rowPrefix: String, count: Int) {
        rule.onAllNodes(hasAnyAncestor(hasTestTag(cardTag)) and SemanticsMatcher("$cardTag rows") {
            it.config.getOrElseNullable(SemanticsProperties.TestTag) { null }?.startsWith(rowPrefix) == true
        }, true).assertCountEquals(count)
    }

    fun taggedRow(cardTag: String, rowTag: String, name: String, count: Int) {
        val row = rule.onNode(hasTestTag(rowTag) and hasAnyAncestor(hasTestTag(cardTag)), true)
        row.assertIsDisplayed()
        row.onChildren().assertCountEquals(2)
        row.onChildren().filter(hasText(name)).assertCountEquals(1)
        row.onChildren().filter(hasText(count.toString())).assertCountEquals(1)
        row.onChildren().filter(hasText(name)).onFirst().assertIsDisplayed()
        row.onChildren().filter(hasText(count.toString())).onFirst().assertIsDisplayed()
    }

    fun singleCountCard(title: String, name: String, count: Int) {
        // Untagged CountCard's Surface exposes title + the two row Text children.
        // Direct-child scope deliberately excludes Summary.List and adjacent cards.
        val card = rule.onNode(hasAnyChild(hasText(title)) and hasAnyChild(hasText(name)), true)
        card.assertIsDisplayed()
        val texts = card.onChildren().filter(SemanticsMatcher.keyIsDefined(SemanticsProperties.Text))
        texts.assertCountEquals(3)
        listOf(title, name, count.toString()).forEach { text ->
            texts.filter(hasText(text)).assertCountEquals(1)
            texts.filter(hasText(text)).onFirst().assertIsDisplayed()
        }
    }
}
