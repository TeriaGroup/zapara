package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.*
import java.time.Instant
import java.time.LocalDate

sealed interface GroupSpaceAction {
    data class Panel(val name: String?) : GroupSpaceAction
    data class Category(val id: String?, val title: String, val position: Int, val revision: Long) : GroupSpaceAction
    data class DeleteCategory(val id: String) : GroupSpaceAction
    data class Archive(val topic: GroupTopic, val archived: Boolean) : GroupSpaceAction
    data object LoadArchive : GroupSpaceAction
    data class LoadAccess(val topicId: String) : GroupSpaceAction
    data class PreviewAccess(val topicId: String, val rules: List<GroupAccessRule>, val revision: Long) : GroupSpaceAction
    data class Access(val topicId: String, val rules: List<GroupAccessRule>, val revision: Long) : GroupSpaceAction
    data object LoadAudit : GroupSpaceAction
    data class Preview(val userId: String?, val roleId: String?) : GroupSpaceAction
    data object EndPreview : GroupSpaceAction
    data class Role(val role: GroupRole) : GroupSpaceAction
    data class CreateRole(val name: String) : GroupSpaceAction
    data class RoleImpact(val roleId: String) : GroupSpaceAction
    data class DeleteRole(val roleId: String) : GroupSpaceAction
    data class Power(val roleId: String, val power: String, val on: Boolean) : GroupSpaceAction
    data class Grant(val roleId: String, val userId: String, val on: Boolean) : GroupSpaceAction
    data class CreateForm(val title: String, val description: String, val deadline: Instant?, val anonymous: Boolean, val questions: List<GroupFormQuestion>) : GroupSpaceAction
    data class SubmitForm(val formId: String, val answers: List<GroupFormAnswer>) : GroupSpaceAction
    data class Responses(val formId: String, val after: String? = null) : GroupSpaceAction
    data class SaveHomework(val id: String?, val title: String, val body: String, val revision: Long, val deadline: Instant?) : GroupSpaceAction
    data class CompleteHomework(val id: String, val on: Boolean) : GroupSpaceAction
    data class ScheduleDate(val date: LocalDate) : GroupSpaceAction
    data object ReloadContent : GroupSpaceAction
}
