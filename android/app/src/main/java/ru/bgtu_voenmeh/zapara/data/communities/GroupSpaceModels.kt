package ru.bgtu_voenmeh.zapara.data.communities

import java.time.Instant

data class GroupCapabilities(val maxRoles: Int = 12, val maxRolesPerMember: Int = 3, val maxTopics: Int = 24, val powers: List<String> = emptyList(), val templates: List<String> = emptyList(), val homeworkAudience: Boolean = false)
data class GroupCategory(val categoryId: String, val title: String, val position: Int, val revision: Long)
data class GroupSpace(val topics: List<GroupTopic>, val categories: List<GroupCategory>, val capabilities: GroupCapabilities, val desk: GroupDesk)
data class GroupAccessRule(val roleId: String?, val power: String, val state: String)
data class GroupTopicAccess(val topicId: String, val revision: Long, val rules: List<GroupAccessRule>)
data class GroupAuditEvent(val eventId: String, val actorId: String?, val action: String, val objectId: String, val createdAt: Instant)
data class GroupRoleImpact(val roleId: String, val assignments: Int, val accessRules: Int)
data class GroupFormQuestion(val questionId: String, val title: String, val kind: String, val required: Boolean, val options: List<String>)
data class GroupFormAnswer(val questionId: String, val text: String?, val choices: List<String> = emptyList())
data class GroupFormResponsesPage(val formId: String, val responses: List<GroupFormResponse>, val nextCursor: String?, val totalResponses: Int)
data class GroupFormResponse(val respondentId: String?, val answers: List<GroupFormAnswer>, val updatedAt: Instant)
data class GroupForm(val formId: String, val topicId: String, val title: String, val description: String, val deadlineAt: Instant?, val anonymous: Boolean, val questions: List<GroupFormQuestion>, val createdBy: String, val createdAt: Instant, val canRespond: Boolean, val canViewResponses: Boolean, val ownResponse: GroupFormResponse?, val responseCount: Int)

object GroupTemplates {
    val titles = linkedMapOf("chat" to "Чат", "announcements" to "Объявления", "polls" to "Опросы", "forms" to "Анкеты", "subject" to "Предмет", "materials" to "Материалы", "homework" to "Домашка", "schedule" to "Расписание")
    fun kind(template: String): String = when(template) { "announcements", "subject" -> "chat"; "polls" -> "ballots"; else -> template }
    val kinds = setOf("chat", "ballots", "forms", "materials", "homework", "schedule")
}

data class GroupAccessPreviewParticipant(val userId: String, val beforePermissions: List<String>, val afterPermissions: List<String>, val sources: Map<String, String>)
data class GroupTopicAccessPreview(val topicId: String, val revision: Long, val affectedCount: Int, val beforeReaders: List<String>, val afterReaders: List<String>, val participants: List<GroupAccessPreviewParticipant>)
data class GroupAccessApproval(val rules: List<GroupAccessRule>, val response: GroupTopicAccessPreview) {
    val addedReaders get() = response.afterReaders.filterNot { it in response.beforeReaders }
    val removedReaders get() = response.beforeReaders.filterNot { it in response.afterReaders }
    fun matches(topicId: String, revision: Long, draft: List<GroupAccessRule>): Boolean = response.topicId == topicId && response.revision == revision && canonical(rules) == canonical(draft)
    private fun canonical(values: List<GroupAccessRule>) = values.sortedWith(compareBy<GroupAccessRule> { it.roleId ?: "" }.thenBy { it.power }.thenBy { it.state })
}
