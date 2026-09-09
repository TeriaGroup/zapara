package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.LocalDate

object HomeworkGroups {
    fun statusOf(status: String): GroupStatus = when (status) {
        "overdue" -> GroupStatus.Overdue
        "burning", "burning_urgent" -> GroupStatus.Burning
        "approaching" -> GroupStatus.Soon
        "done" -> GroupStatus.Done
        else -> GroupStatus.Later
    }

    fun title(status: GroupStatus, copy: UiCopy): String = copy.get(
        when (status) {
            GroupStatus.Overdue -> "hw_group_overdue"
            GroupStatus.Burning -> "hw_group_burning"
            GroupStatus.Soon -> "hw_group_soon"
            GroupStatus.Later -> "hw_group_later"
            GroupStatus.Done -> "hw_group_done"
        }
    )

    fun dueLabel(hw: Homework, today: LocalDate, copy: UiCopy): String = when {
        hw.done || hw.status == "done" -> copy.get("hw_done")
        hw.status == "overdue" -> copy.get("hw_overdue_since", hw.due?.let(LessonFormat::dayMonth) ?: "")
        else -> hw.due?.let { copy.get("hw_due", LessonFormat.dayMonth(it), LessonFormat.weekdayShort(it, copy)) }
            ?: copy.get("hw_due_none")
    }

    fun toItem(hw: Homework, subject: String, today: LocalDate, copy: UiCopy, subjectRaw: String = "") = HomeworkItemUi(
        id = hw.id, subject = subject, text = hw.text, dueLabel = dueLabel(hw, today, copy),
        status = hw.status, done = hw.done, subjectRaw = subjectRaw, n = hw.n
    )

    fun group(items: List<HomeworkItemUi>, copy: UiCopy): List<HomeworkGroupUi> {
        if (items.isEmpty()) return emptyList()
        return GroupStatus.entries.mapNotNull { status ->
            val bucket = items.filter { statusOf(it.status) == status }
                .sortedWith(compareBy<HomeworkItemUi> { burnRank(it.status) }.thenBy { it.dueLabel })
            if (bucket.isEmpty()) null
            else HomeworkGroupUi(status, title(status, copy), bucket, collapsed = status == GroupStatus.Done)
        }
    }

    private fun burnRank(status: String): Int = when (status) {
        "burning_urgent" -> 0
        "burning" -> 1
        else -> 2
    }
}
