package ru.bgtu_voenmeh.zapara.ui.homework

import java.util.Locale

enum class HomeworkCompletionFilter { Active, Done, All }
enum class HomeworkDeadlineFilter { All, Overdue, Urgent, Soon, NoDate }
enum class HomeworkOriginFilter { All, Personal, Shared }

data class HomeworkBrowseResult(
    val groups: List<HomeworkGroupUi>,
    val totalActive: Int,
    val totalDone: Int
) {
    val visibleCount: Int get() = groups.sumOf { it.items.size }
}

object HomeworkBrowse {
    fun shared(rows: List<SharedHomeworkItemUi>, query: String,
        completion: HomeworkCompletionFilter, origin: HomeworkOriginFilter = HomeworkOriginFilter.All,
        deadline: HomeworkDeadlineFilter = HomeworkDeadlineFilter.All,
        withFiles: Boolean = false): List<SharedHomeworkItemUi> {
        if (origin == HomeworkOriginFilter.Personal || deadline != HomeworkDeadlineFilter.All || withFiles) return emptyList()
        val words = query.trim().lowercase(Locale.ROOT).split(Regex("\\s+")).filter { it.isNotEmpty() }
        return rows.filter { row ->
            (completion == HomeworkCompletionFilter.All || row.completed == (completion == HomeworkCompletionFilter.Done)) &&
                words.all { word -> row.title.lowercase(Locale.ROOT).contains(word) ||
                    row.body.lowercase(Locale.ROOT).contains(word) ||
                    row.deadlineLabel.lowercase(Locale.ROOT).contains(word) }
        }
    }

    fun filter(groups: List<HomeworkGroupUi>, query: String, completion: HomeworkCompletionFilter,
        origin: HomeworkOriginFilter = HomeworkOriginFilter.All,
        deadline: HomeworkDeadlineFilter = HomeworkDeadlineFilter.All,
        withFiles: Boolean = false, sortBySubject: Boolean = false): HomeworkBrowseResult {
        val totalActive = groups.sumOf { group -> group.items.count { !it.done } }
        val totalDone = groups.sumOf { group -> group.items.count { it.done } }
        val words = query.trim().lowercase(Locale.ROOT).split(Regex("\\s+")).filter { it.isNotEmpty() }
        val visible = groups.takeUnless { origin == HomeworkOriginFilter.Shared }.orEmpty().mapNotNull { group ->
            val rows = group.items.filter { item ->
                (completion == HomeworkCompletionFilter.All || item.done == (completion == HomeworkCompletionFilter.Done)) &&
                    (!withFiles || item.files.isNotEmpty()) &&
                    when (deadline) {
                        HomeworkDeadlineFilter.All -> true
                        HomeworkDeadlineFilter.Overdue -> group.status == GroupStatus.Overdue
                        HomeworkDeadlineFilter.Urgent -> group.status == GroupStatus.Burning
                        HomeworkDeadlineFilter.Soon -> group.status == GroupStatus.Soon
                        HomeworkDeadlineFilter.NoDate -> item.due == null
                    } &&
                    words.all { word ->
                        item.subject.lowercase(Locale.ROOT).contains(word) ||
                            item.subjectRaw.lowercase(Locale.ROOT).contains(word) ||
                            item.text.lowercase(Locale.ROOT).contains(word) ||
                            item.dueLabel.lowercase(Locale.ROOT).contains(word) ||
                            item.statusLabel.lowercase(Locale.ROOT).contains(word) ||
                            item.files.any { it.name.lowercase(Locale.ROOT).contains(word) }
                    }
            }
            if (rows.isEmpty()) null else group.copy(items = if (sortBySubject)
                rows.sortedWith(compareBy<HomeworkItemUi> { it.subject.lowercase(Locale.ROOT) }
                    .thenBy { it.due ?: java.time.LocalDate.MAX }.thenBy { it.id }) else rows)
        }
        return HomeworkBrowseResult(visible, totalActive, totalDone)
    }
}
