package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate

data class HomeworkRescheduleRow(val before: Homework, val subject: String, val afterDue: LocalDate?,
    val status: String = "pending") {
    val eligible: Boolean get() = !before.done && before.n < 10 && before.due != null && afterDue != null && afterDue > before.due
    fun matches(current: Homework?): Boolean = current != null && current.id == before.id && current.norm == before.norm &&
        current.text == before.text && current.n == before.n && current.createdAt == before.createdAt &&
        current.due == before.due && current.done == before.done
    fun canUndo(current: Homework?): Boolean = current != null && current.id == before.id && current.norm == before.norm &&
        current.text == before.text && current.n == before.n + 1 && current.createdAt == before.createdAt &&
        current.due == afterDue && !current.done
}
data class HomeworkRescheduleBatch(val groupId: String, val profileName: String, val epoch: Long,
    val rows: List<HomeworkRescheduleRow>, val busy: Boolean = false)

internal class HomeworkRescheduleConflict : IllegalStateException("homework_reschedule_changed")

/** Must execute inside the caller's Room transaction; a failed post-write check must roll it back. */
internal fun applyVerifiedReschedule(row: HomeworkRescheduleRow, undo: Boolean,
    read: () -> Homework?, compute: (Int) -> LocalDate?, write: (String, Int) -> Unit,
    scopeCurrent: () -> Boolean = { true }): Boolean {
    if (!scopeCurrent()) return false
    val current = read()
    if (!(if (undo) row.canUndo(current) else row.matches(current))) return false
    val target = if (undo) row.before.n else row.before.n + 1
    val expected = if (undo) row.before.due else row.afterDue
    if (compute(target) != expected) return false
    write(row.before.text, target)
    val persisted = read()
    if (!scopeCurrent() || !(if (undo) row.matches(persisted) else row.canUndo(persisted)))
        throw HomeworkRescheduleConflict()
    return true
}
