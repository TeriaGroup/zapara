package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.compose.runtime.saveable.Saver
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience
import java.util.UUID

/** Keep recipient and request identity state tied to the community and editor. */
internal object GroupHomeworkDraftRules {
    val AudienceSaver = Saver<HomeworkAudience, ArrayList<String>>(
        save = { saveAudience(it) },
        restore = { restoreAudience(it) }
    )

    fun scope(communityId: String?, topicId: String?, homeworkId: String?): String =
        listOf(communityId.orEmpty(), topicId.orEmpty(), homeworkId.orEmpty()).joinToString("|")

    fun saveAudience(value: HomeworkAudience): ArrayList<String> = arrayListOf<String>().also { saved ->
        saved.add(value.kind)
        saved.add(value.roleIds.size.toString())
        saved.addAll(value.roleIds)
        saved.add(value.userIds.size.toString())
        saved.addAll(value.userIds)
    }

    fun restoreAudience(saved: List<String>): HomeworkAudience {
        val invalid = HomeworkAudience("selected")
        if (saved.size < 3) return invalid
        val roleCount = saved[1].toIntOrNull()?.takeIf { it in 0..100 } ?: return invalid
        val userCountAt = 2 + roleCount
        if (saved.size <= userCountAt) return invalid
        val userCount = saved[userCountAt].toIntOrNull()?.takeIf { it in 0..500 } ?: return invalid
        if (saved.size != userCountAt + 1 + userCount) return invalid
        val roles = saved.subList(2, userCountAt)
        val users = saved.subList(userCountAt + 1, saved.size)
        if ((roles + users).any { value -> runCatching { UUID.fromString(value).toString().equals(value, ignoreCase = true) }.getOrDefault(false).not() }) return invalid
        val value = HomeworkAudience(saved[0], roles.toList(), users.toList())
        return if (value.valid()) value else invalid
    }

    fun nextOperation(current: String, confirmedOperation: String?, fresh: () -> String): String =
        if (confirmedOperation == current) fresh() else current

    fun conflict(draft: HomeworkAudience, server: HomeworkAudience?): AudienceConflict =
        AudienceConflict(draft, server ?: HomeworkAudience())

    data class AudienceConflict(val draft: HomeworkAudience, val server: HomeworkAudience) {
        val sameAudience: Boolean get() = draft == server
        fun reloadAudience(): HomeworkAudience = server
        fun keepDraftAudience(): HomeworkAudience = draft
    }
}
