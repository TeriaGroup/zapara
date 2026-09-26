package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import java.time.LocalDate
import java.time.LocalDateTime

data class GroupSubjectHomeworkUi(val localId: Long?, val sharedId: String?, val title: String, val body: String, val due: LocalDate?, val done: Boolean)
data class GroupSubjectContext(val lesson: GroupLessonHint?, val personal: List<GroupSubjectHomeworkUi>)
object GroupSubjectLogic {
    fun matches(bound: String, raw: String): Boolean = Parity.normalizeSubject(LessonFormat.stripType(bound,"")) == Parity.normalizeSubject(LessonFormat.stripType(raw,""))
    fun nextLesson(group: String?, subject: String, now: LocalDateTime, lessonsByDate: (LocalDate)->List<Lesson>): GroupLessonHint? =
        nextGroupLesson(group,group,now) { date -> lessonsByDate(date).filter { matches(subject,it.subjectRaw) || matches(subject,it.subjectNormalized) } }
}
