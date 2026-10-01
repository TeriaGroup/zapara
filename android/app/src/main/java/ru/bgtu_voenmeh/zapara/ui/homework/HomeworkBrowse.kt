package ru.bgtu_voenmeh.zapara.ui.homework

import java.util.Locale

enum class HomeworkCompletionFilter { Active, Done, All }

data class HomeworkBrowseResult(
    val groups: List<HomeworkGroupUi>,
    val totalActive: Int,
    val totalDone: Int
) {
    val visibleCount: Int get() = groups.sumOf { it.items.size }
}

object HomeworkBrowse {
    fun shared(rows: List<SharedHomeworkItemUi>, query: String,
        completion: HomeworkCompletionFilter): List<SharedHomeworkItemUi> {
        val words = query.trim().lowercase(Locale.ROOT).split(Regex("\\s+")).filter { it.isNotEmpty() }
        return rows.filter { row ->
            (completion == HomeworkCompletionFilter.All || row.completed == (completion == HomeworkCompletionFilter.Done)) &&
                words.all { word -> row.title.lowercase(Locale.ROOT).contains(word) ||
                    row.body.lowercase(Locale.ROOT).contains(word) }
        }
    }

    fun filter(groups: List<HomeworkGroupUi>, query: String, completion: HomeworkCompletionFilter): HomeworkBrowseResult {
        val totalActive = groups.sumOf { group -> group.items.count { !it.done } }
        val totalDone = groups.sumOf { group -> group.items.count { it.done } }
        val words = query.trim().lowercase(Locale.ROOT).split(Regex("\\s+")).filter { it.isNotEmpty() }
        val visible = groups.mapNotNull { group ->
            val rows = group.items.filter { item ->
                (completion == HomeworkCompletionFilter.All || item.done == (completion == HomeworkCompletionFilter.Done)) &&
                    words.all { word ->
                        item.subject.lowercase(Locale.ROOT).contains(word) ||
                            item.subjectRaw.lowercase(Locale.ROOT).contains(word) ||
                            item.text.lowercase(Locale.ROOT).contains(word)
                    }
            }
            if (rows.isEmpty()) null else group.copy(items = rows)
        }
        return HomeworkBrowseResult(visible, totalActive, totalDone)
    }
}
