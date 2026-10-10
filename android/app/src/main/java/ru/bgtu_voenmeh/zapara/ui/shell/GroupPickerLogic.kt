package ru.bgtu_voenmeh.zapara.ui.shell

import android.content.Context
import ru.bgtu_voenmeh.zapara.data.GroupInfo

/** #108 / AN-19: выбор группы — недавние сверху, дальше по факультетам (как desktop #21: «Факультет И» по первой букве). */
object GroupPickerLogic {
    const val RECENT_MAX = 3

    sealed interface Header {
        data object Recent : Header
        data class Faculty(val letter: String) : Header
        data object Other : Header
    }

    data class Block(val header: Header, val groups: List<GroupInfo>)

    fun faculty(name: String): Header {
        val first = name.trim().firstOrNull()
        return if (first != null && first.isLetter()) Header.Faculty(first.uppercaseChar().toString()) else Header.Other
    }

    /** Новый список недавних: выбранная группа первой, без повторов, не больше [RECENT_MAX]. */
    fun pushRecent(recent: List<String>, picked: String): List<String> =
        (listOf(picked) + recent.filterNot { it == picked }).take(RECENT_MAX)

    /**
     * Блоки списка. Без поиска: «Недавние» (текущая группа и недавние, которые есть в каталоге), затем остальные
     * по факультетам. С поиском — только найденные, по факультетам. Порядок групп внутри блока — как в [groups].
     */
    fun blocks(groups: List<GroupInfo>, currentId: String?, recentIds: List<String>, searching: Boolean): List<Block> {
        val byId = groups.associateBy { it.id }
        val recent = if (searching) emptyList() else
            (listOfNotNull(currentId) + recentIds).distinct().mapNotNull(byId::get).take(RECENT_MAX)
        val shown = recent.map { it.id }.toSet()
        val rest = groups.filterNot { it.id in shown }.groupBy { faculty(it.name) }
        val faculties = rest.keys.filterIsInstance<Header.Faculty>().sortedBy { it.letter }
        return buildList {
            if (recent.isNotEmpty()) add(Block(Header.Recent, recent))
            faculties.forEach { add(Block(it, rest.getValue(it))) }
            rest[Header.Other]?.let { add(Block(Header.Other, it)) }
        }
    }
}

/**
 * Недавние группы на устройстве (SharedPreferences, не синхронизируются) — отдельно для каждого профиля.
 * Пишет ShellViewModel только после успешного выбора группы.
 */
class RecentGroups internal constructor(private val read: (String) -> String?, private val write: (String, String) -> Unit) {
    fun ids(profile: String): List<String> = read(key(profile)).orEmpty().split(',').filter(String::isNotBlank)
    fun push(profile: String, id: String) = write(key(profile), GroupPickerLogic.pushRecent(ids(profile), id).joinToString(","))

    companion object {
        fun key(profile: String) = "recent:$profile"
        fun of(context: Context): RecentGroups {
            val prefs = context.getSharedPreferences("group_picker", Context.MODE_PRIVATE)
            return RecentGroups({ prefs.getString(it, null) }, { k, v -> prefs.edit().putString(k, v).apply() })
        }
    }
}
