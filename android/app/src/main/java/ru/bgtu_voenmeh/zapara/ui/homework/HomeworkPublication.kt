package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

data class PersonalPublicationRow(val before: Homework, val title: String,
    val deadline: Instant?, val operationId: String = UUID.randomUUID().toString(),
    val attempted: Boolean = false, val sent: Boolean = false, val failed: Boolean = false) {
    fun matches(current: Homework?): Boolean = current != null && before.id == current.id && before.norm == current.norm &&
        before.text == current.text && before.createdAt == current.createdAt && before.n == current.n &&
        before.due == current.due && !current.done
}
data class PersonalPublicationBatch(val groupId: String, val profileName: String, val epoch: Long,
    val context: HomeworkShareContext, val authorId: String, val rows: List<PersonalPublicationRow>,
    val audience: HomeworkAudience = HomeworkAudience(), val busy: Boolean = false, val open: Boolean = true,
    val error: String? = null) {
    val locked: Boolean get() = rows.any { it.attempted }
}

internal fun personalPublicationDeadline(date: java.time.LocalDate?): Instant? = date?.atTime(23,59,59)
    ?.atZone(ZoneId.of("Europe/Moscow"))?.toInstant()

internal fun publicationAudienceAvailable(audience: HomeworkAudience, context: HomeworkShareContext): Boolean =
    audience.valid() && context.supported &&
        audience.roleIds.all { id -> context.desk.roles.any { it.roleId == id } } &&
        audience.userIds.all { id -> context.people.any { it.userId == id } }
