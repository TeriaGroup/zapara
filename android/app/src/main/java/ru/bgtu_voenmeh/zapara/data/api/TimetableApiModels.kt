package ru.bgtu_voenmeh.zapara.data.api

import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Lesson
import java.time.LocalDate

data class TimetableApiPeriod(val start: LocalDate, val weekCount: Int, val title: String, val timeZone: String)

data class TimetableApiMeta(
    val snapshotId: String,
    val fetchedAt: String,
    val publishedAt: String,
    val sourceModifiedAt: String?,
    val sourceKind: String,
    val sourceUrl: String?,
    val sourceSha256: String,
    val stale: Boolean
)

data class TimetableApiRefresh(
    val lastAttemptId: String?,
    val lastAttemptStatus: String?,
    val lastSuccessAt: String?,
    val lastFailureAt: String?,
    val lastFailureCode: String?,
    val abandoned: Boolean
)

data class TimetableApiGroup(val id: String, val name: String, val lessonCount: Int) {
    fun toGroup() = GroupInfo(id, name, "")
}

data class TimetableApiLesson(
    val dayOfWeek: Int,
    val parity: Int,
    val index: Int,
    val timeStart: String,
    val timeEnd: String,
    val subjectRaw: String,
    val subjectNormalized: String,
    val typeRaw: String?,
    val teacherRaw: String?,
    val classroomRaw: String?,
    val roomRaw: String?,
    val buildingRaw: String?
) {
    fun toLesson(groupId: String) = Lesson(
        groupId = groupId,
        dayOfWeek = dayOfWeek,
        parity = parity,
        index = index,
        timeStart = timeStart,
        timeEnd = timeEnd,
        subjectRaw = subjectRaw,
        subjectNormalized = subjectNormalized,
        teacherRaw = teacherRaw.orEmpty(),
        roomRaw = roomRaw.orEmpty(),
        buildingRaw = buildingRaw.orEmpty(),
        typeRaw = typeRaw.orEmpty(),
        classroomRaw = classroomRaw.orEmpty()
    )
}

data class TimetableApiDownloadedGroup(
    val group: TimetableApiGroup,
    val meta: TimetableApiMeta,
    val refresh: TimetableApiRefresh,
    val lessons: List<TimetableApiLesson>
)

/** Absent map entry means unfetched, not an empty/current timetable. */
data class TimetableApiSnapshot(
    val period: TimetableApiPeriod,
    val meta: TimetableApiMeta,
    val refresh: TimetableApiRefresh,
    val groups: List<TimetableApiGroup>,
    val downloaded: Map<String, TimetableApiDownloadedGroup>
)

enum class TimetableApiFailure {
    InvalidPayload, UnknownRequiredGroup, SnapshotUnavailable, ServerUnavailable, BodyTooLarge, Timeout, Transport
}

class TimetableApiException(val failure: TimetableApiFailure) :
    Exception("Не удалось получить проверенное расписание. Локальные данные сохранены.")

data class CacheMetadata(
    val period: TimetableApiPeriod,
    val meta: TimetableApiMeta?,
    val fetchedAt: String?,
    val source: String,
    val sourceBase: String
)
