@file:Suppress("INVISIBLE_MEMBER", "INVISIBLE_REFERENCE")

package ru.bgtu_voenmeh.zapara

import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.ComposeTestRule
import ru.bgtu_voenmeh.zapara.ui.homework.GroupStatus

/** Uses the accepted window-origin measurement, with Homework's real untagged LazyColumn. */
internal class HomeworkCaptureAssertions(private val rule: ComposeTestRule) {
    fun list() = rule.onNode(hasScrollToIndexAction())
    fun bounds(node: SemanticsNode): SummaryBounds {
        val c = node.layoutNode.innerCoordinator
        check(c.isAttached)
        val p = c.positionInWindow()
        return summaryRootWindowBounds(p.x, p.y, c.size.width.toFloat(), c.size.height.toFloat())
    }
    fun rootViewport(editor: Boolean): SummaryBounds {
        val node = if (editor) rule.onNodeWithTag("Editor.Text").fetchSemanticsNode() else list().fetchSemanticsNode()
        val root = generateSequence(node) { it.parent }.last()
        return rule.runOnIdle { bounds(root) }
    }
    fun viewport(): SummaryBounds = list().fetchSemanticsNode().boundsInWindow.let {
        SummaryBounds(it.left, it.top, it.right, it.bottom)
    }
    fun insideList(node: SemanticsNode) = generateSequence(node.parent) { it.parent }.any {
        it.config.contains(SemanticsActions.ScrollToIndex)
    }
    fun scroll(): SummaryScroll {
        val range = list().fetchSemanticsNode().config[SemanticsProperties.VerticalScrollAxisRange]
        return rule.runOnIdle { SummaryScroll(range.value(), range.maxValue(), range.value() < range.maxValue()) }
    }
    fun row(id: Long) {
        val index = HomeworkCaptureModel.records.indexOfFirst { it.id == id }
        rule.onNodeWithTag("Homework.Row.$id").assertIsDisplayed()
        rule.onNode(hasTestTag("Homework.Due.$id") and hasAnyAncestor(hasTestTag("Homework.Row.$id")), true)
            .assertTextEquals(HomeworkCaptureModel.expectedDue[index]).assertIsDisplayed()
        rule.onNode(hasTestTag("Homework.Status.$id") and hasAnyAncestor(hasTestTag("Homework.Row.$id")), true)
            .assertTextEquals(HomeworkCaptureModel.expectedStatus[index]).assertIsDisplayed()
        rule.onNode(hasText("Конспект $id") and hasAnyAncestor(hasTestTag("Homework.Row.$id")), true).assertIsDisplayed()
    }
    fun group(status: GroupStatus) {
        val index = GroupStatus.entries.indexOf(status)
        val title = listOf("Просрочено", "Горит", "Скоро", "Дальше", "Сдано")[index]
        val count = listOf(1, 2, 1, 2, 1)[index]
        rule.onNode(hasText("$title · $count") and hasAnyAncestor(hasTestTag("Homework.Group.$status")), true).assertIsDisplayed()
    }
    fun observe(tags: List<String>, root: SummaryBounds, scale: Float): List<SummaryBounds>? {
        val nodes = tags.map { rule.onAllNodes(hasTestTag(it), true).fetchSemanticsNodes().singleOrNull() ?: return null }
        val boxes = rule.runOnIdle { nodes.map(::bounds) }
        if (!boxes.all(root::contains)) return null
        if (nodes.zip(boxes).any { (node, box) -> insideList(node) && !viewport().contains(box) }) return null
        fun verify(node: SemanticsNode) {
            if (node.config.contains(SemanticsActions.GetTextLayoutResult)) RenderedTextEvidence.check(node, scale)
            node.children.forEach(::verify)
        }
        rule.runOnIdle { nodes.forEach(::verify) }
        return boxes
    }
}
