package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic

class GroupBrowseTest {
    private val general = topic(null, "Общий поток", "chat", unread = 0)
    private val pinned = topic("pinned", "Объявления", "chat", pinned = true)
    private val unread = topic("unread", "Учёба", "chat", unread = 3)
    private val quiet = topic("quiet", "Конспекты", "chat")
    private val ballot = topic("vote", "Опросы", "ballots", unread = 1, description = "После пар")
    private val materials = topic("materials", "Архив", "materials", unread = 1, description = "Лекции")
        .copy(lastBody = "Всё к экзамену", subject = "Физика", categoryId = "cat")
    private val forms = topic("forms", "Формы", "forms")
    private val homework = topic("homework", "Задания", "homework")
    private val schedule = topic("schedule", "План", "schedule")

    @Test fun groups_and_people_search_by_visible_names_and_handles() {
        val groups = listOf(GroupCommunityUi("a", "О3313", "member"), GroupCommunityUi("b", "Военмех", "member"))
        assertEquals(listOf("b"), browseCommunities(groups, " ВОЕН ").map { it.id })
        val people = listOf(
            GroupPersonUi("a", "Максим", "max", "member", false),
            GroupPersonUi("b", "Глеб Бова", "gleb42", "member", false)
        )
        assertEquals(listOf("b"), browsePeople(people, "ГЛЕБ").map { it.id })
        assertEquals(listOf("b"), browsePeople(people, "gleb42").map { it.id })
    }

    @Test fun general_then_pinned_then_unread_preserves_order_within_each_group() {
        assertEquals(listOf(null, "pinned", "unread", "vote", "quiet"),
            browseChannels(listOf(quiet, unread, pinned, general, ballot)).map { it.topicId })
    }

    @Test fun channel_search_type_and_unread_filters_work_together() {
        val rows = listOf(general, pinned, unread, quiet, ballot)
        assertEquals(listOf("vote"), browseChannels(rows, "  ПОСЛЕ  ", "ballots", true).map { it.topicId })
        assertEquals(listOf("unread"), browseChannels(rows, "уч", "chat", true).map { it.topicId })
        assertEquals(emptyList<String>(), browseChannels(rows, "нет совпадений").map { it.topicId })
    }

    @Test fun supported_specialized_channel_kinds_filter_independently() {
        val rows = listOf(general, ballot, materials, forms, homework, schedule)
        assertEquals(listOf("materials"), browseChannels(rows, kind = "materials").map { it.topicId })
        assertEquals(listOf("forms"), browseChannels(rows, kind = "forms").map { it.topicId })
        assertEquals(listOf("homework"), browseChannels(rows, kind = "homework").map { it.topicId })
        assertEquals(listOf("schedule"), browseChannels(rows, kind = "schedule").map { it.topicId })
    }

    @Test fun channel_search_matches_normalized_subject_category_and_latest_body_tokens() {
        val rows = listOf(materials, materials.copy(topicId = "hidden", unread = 0), quiet)
        val categoryTitles = mapOf("cat" to "Преподаватели")
        assertEquals(listOf("materials"), browseChannels(rows, "физика преподаватели все", unreadOnly = true,
            categoryTitles = categoryTitles).map { it.topicId })
        assertEquals(listOf("materials", "hidden"), browseChannels(rows, "всё физика", categoryTitles = categoryTitles).map { it.topicId })
        assertEquals(emptyList<String>(), browseChannels(rows, "физика преподаватели несуществует",
            categoryTitles = categoryTitles).map { it.topicId })
    }

    private fun topic(id: String?, title: String, kind: String, unread: Int = 0,
                      pinned: Boolean = false, description: String = "") = GroupTopic(
        id, title, "💬", kind, null, null, null, unread, false, 0,
        description = description, pinned = pinned
    )
}
