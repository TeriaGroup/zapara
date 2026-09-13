package ru.bgtu_voenmeh.zapara

import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.ComposeTestRule
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.ui.homework.GroupStatus

/** No accepted coverage here: only current-frame references for subsequent runtime review. */
internal class HomeworkCaptureActions(private val rule: ComposeTestRule) {
    val checks = HomeworkCaptureAssertions(rule)
    private val pending = HomeworkCaptureModel.obligations.toMutableSet()
    private var obligation = ""
    private var targets = emptyList<String>()
    var editor = false
        private set

    fun observeFrame(scale: Float): JSONObject {
        val root = checks.rootViewport(editor)
        val scroll = if (editor) null else checks.scroll()
        val viewport = if (editor) null else checks.viewport()
        val observed = linkedMapOf(obligation to requireNotNull(checks.observe(targets, root, scale)) {
            "Homework target not fully visible: $obligation"
        })
        if (!editor) HomeworkCaptureModel.records.forEach { row ->
            val id = "row-${row.id}"
            if (id in pending && id !in observed && obligation !in listOf("toggle-done")) {
                checks.observe(listOf("Homework.Row.${row.id}"), root, scale)?.let {
                    checks.row(row.id)
                    observed[id] = it
                }
            }
        }
        check(root == checks.rootViewport(editor) && (editor || (scroll == checks.scroll() && viewport == checks.viewport()))) {
            "Homework frame moved during observation"
        }
        pending.removeAll(observed.keys)
        fun box(b: SummaryBounds) = JSONObject().put("left", b.left).put("top", b.top).put("right", b.right).put("bottom", b.bottom)
        return JSONObject().put("rootWindowBounds", box(root)).put("listWindowBounds", viewport?.let(::box))
            .put("obligations", JSONArray().apply { observed.forEach { (id, boxes) ->
                put(JSONObject().put("id", id).put("bounds", JSONArray().apply { boxes.forEach { put(box(it)) } }))
            } }).put("pending", JSONArray(pending.toList())).put("accepted", false).put("savePersistenceProven", false)
    }

    fun capture(fixture: UiMapsCaptureFixtures, shot: (String, () -> Unit) -> Unit) {
        fun frame(id: String, tags: List<String>, inEditor: Boolean = false, action: () -> Unit) {
            if (id !in pending) return
            obligation = id; targets = tags; editor = inEditor
            shot(id, action)
        }
        frame("top", listOf("Top.Title")) { rule.onNodeWithTag("Top.Title").assertTextEquals("Домашка") }
        GroupStatus.entries.forEach { status ->
            frame("group-$status", listOf("Homework.Group.$status")) {
                rule.onNodeWithTag("Homework.Group.$status").performScrollTo()
                if (status == GroupStatus.Later) {
                    rule.onNodeWithTag("Homework.Group.$status").performClick().performClick()
                } else if (status == GroupStatus.Done) {
                    rule.onNodeWithTag("Homework.Group.$status").performClick()
                }
                checks.group(status)
                rule.runOnIdle { assertFalse(fixture.homework.groups.single { it.status == status }.collapsed) }
            }
        }
        HomeworkCaptureModel.records.forEach { row ->
            frame("row-${row.id}", listOf("Homework.Row.${row.id}")) {
                rule.onNodeWithTag("Homework.Row.${row.id}").performScrollTo()
                checks.row(row.id)
            }
        }
        frame("bottom", listOf("Homework.Row.107")) {
            rule.onNodeWithTag("Homework.Row.107").performScrollTo()
            driveSummaryBottom(read = checks::scroll, advance = {
                val distance = checks.viewport().let { (it.bottom - it.top) / 2 }
                checks.list().performSemanticsAction(SemanticsActions.ScrollBy) { check(it(0f, distance)) }
                rule.waitForIdle()
            })
            assertFalse(checks.scroll().canScrollForward)
            checks.row(107)
        }
        listOf("toggle-done", "toggle-restored").forEachIndexed { index, id ->
            frame(id, listOf("Homework.Row.101")) {
                rule.onNodeWithTag("Homework.Row.101").performScrollTo()
                rule.onNodeWithTag("Homework.Done.101").performClick()
                rule.onNodeWithTag("Homework.Row.101").performScrollTo()
                rule.onNodeWithTag("Homework.Due.101", true).assertTextEquals("срок 11.09 (Пт)")
                rule.onNodeWithTag("Homework.Status.101", true).assertTextEquals(if (index == 0) "Сдано" else "Просрочено")
            }
        }
        listOf(101L, 106L).forEach { id ->
            val stored = HomeworkCaptureModel.records.single { it.id == id }
            val oldDue = if (id == 101L) "Срок: 11.09 (Пт)" else "Срок: —"
            val controls = listOf("Editor.Text", "Editor.Due", "Editor.Inc", "Editor.Dec", "Editor.Cancel", "Editor.Save")
            fun edit(stage: String, due: String, action: () -> Unit) = frame("editor-$id-$stage", controls, true) {
                action()
                rule.onNodeWithTag("Editor.Due", true).assertTextEquals(due)
                rule.onNodeWithTag("Editor.Save").assertIsEnabled()
            }
            fun open() { rule.onNodeWithTag("Homework.Row.$id").performScrollTo().performClick() }
            edit("open", oldDue) { open() }
            edit("text", "Срок: 02.09 (Ср)") { rule.onNodeWithTag("Editor.Text").performTextReplacement("Изменённый конспект") }
            edit("n", "Срок: 03.09 (Чт)") { rule.onNodeWithTag("Editor.Inc").performClick() }
            edit("n-return", "Срок: 02.09 (Ср)") { rule.onNodeWithTag("Editor.Dec").performClick() }
            edit("revert", oldDue) { rule.onNodeWithTag("Editor.Text").performTextReplacement(stored.text) }
            frame("editor-$id-cancel", listOf("Homework.Row.$id")) {
                rule.onNodeWithTag("Editor.Cancel").performClick()
                rule.onNodeWithTag("Sheet.Homework").assertDoesNotExist()
                rule.onNodeWithTag("Homework.Row.$id").performScrollTo(); checks.row(id)
            }
            edit("reopen", oldDue) { open() }
            frame("editor-$id-save-callback", listOf("Homework.Row.$id")) {
                val before = fixture.homeworkSaveCallbacks
                rule.onNodeWithTag("Editor.Save").performClick()
                rule.runOnIdle { assertEquals(before + 1, fixture.homeworkSaveCallbacks); assertNull(fixture.homework.editor) }
                rule.onNodeWithTag("Homework.Row.$id").performScrollTo(); checks.row(id)
            }
        }
        check(pending.isEmpty()) { "Unobserved homework obligations: $pending" }
    }
}
