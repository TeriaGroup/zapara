package ru.bgtu_voenmeh.zapara.data.api

import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.db.ApiCacheMetadataEntity
import ru.bgtu_voenmeh.zapara.data.db.ApiCatalogEntity
import ru.bgtu_voenmeh.zapara.data.db.GroupEntity
import ru.bgtu_voenmeh.zapara.data.db.LessonEntity
import ru.bgtu_voenmeh.zapara.data.db.SettingsEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import java.time.LocalDate

class RoomTimetableStore(private val db: ZaparaDatabase) : TimetableStore {
    override var useApiCatalog: Boolean = false

    override fun runInTransaction(block: () -> Unit) {
        db.runInTransaction(block)
    }

    override fun settings(): ScheduleRepository.SettingsState {
        val raw = rawSettings()
        return overlaySettings(this, raw)
    }

    override fun saveSettings(state: ScheduleRepository.SettingsState) {
        db.settingsDao().save(
            SettingsEntity(
                myGroupId = state.myGroupId,
                parityInvert = state.parityInvert,
                language = state.language,
                periodStart = state.periodStart.toString(),
                weekCount = state.weekCount,
                periodTitle = state.periodTitle,
                lastFetchedAt = state.lastFetchedAt,
                intersectionStrictness = state.intersectionStrictness,
                alwaysShowAllTrafficLights = state.alwaysShowAllTrafficLights,
                notifyEnabled = state.notifyEnabled,
                notifyTime1 = state.notifyTime1,
                notifyTime2 = state.notifyTime2,
                theme = state.theme,
                animations = state.animations,
                useUniversityXml = state.useUniversityXml
            )
        )
    }

    override fun groups(): List<GroupInfo> {
        val catalogOn = useApiCatalog && readMetadata("") != null
        val all = db.groupDao().getAll().map { GroupInfo(it.id, it.name, it.url) }
        if (!catalogOn) return all
        val ids = catalogIds()
        return all.filter { it.id in ids }
    }

    override fun storedGroups(): List<GroupInfo> =
        db.groupDao().getAll().map { GroupInfo(it.id, it.name, it.url) }

    override fun upsertGroup(group: GroupInfo) {
        db.groupDao().upsert(GroupEntity(group.id, group.name, group.url))
        db.apiCacheMetadataDao().delete(group.id)
    }

    override fun ensureGroup(id: String, name: String, url: String?) {
        val current = db.groupDao().getById(id)
        db.groupDao().upsert(GroupEntity(id, name, url ?: current?.url.orEmpty()))
    }

    override fun allLessons(groupId: String): List<Lesson> =
        db.lessonDao().getAllForGroup(groupId).map { it.toLesson() }

    override fun replaceLessons(groupId: String, lessons: List<Lesson>) {
        db.lessonDao().clearForGroup(groupId)
        if (lessons.isNotEmpty()) db.lessonDao().insertAll(lessons.map { it.toEntity() })
    }

    override fun clearCatalog() {
        db.apiCatalogDao().clear()
    }

    override fun insertCatalog(groupId: String, name: String) {
        db.apiCatalogDao().insert(ApiCatalogEntity(groupId, name))
    }

    override fun catalogIds(): Set<String> = db.apiCatalogDao().getAll().map { it.groupId }.toSet()

    override fun readMetadata(groupId: String): CacheMetadata? =
        db.apiCacheMetadataDao().get(groupId)?.payload?.let { decodeCacheMetadata(it) }

    override fun writeMetadata(groupId: String, metadata: CacheMetadata) {
        db.apiCacheMetadataDao().upsert(
            ApiCacheMetadataEntity(groupId, metadata.meta?.snapshotId, metadata.encode())
        )
    }

    override fun friends(): List<Friend> = db.friendDao().getAll().map {
        Friend(it.groupName, it.colorHex, it.enabled, it.memberNames)
    }

    override fun dump(): String = ""

    private fun rawSettings(): ScheduleRepository.SettingsState {
        val s = db.settingsDao().get() ?: return ScheduleRepository.SettingsState()
        return ScheduleRepository.SettingsState(
            myGroupId = s.myGroupId,
            parityInvert = s.parityInvert,
            language = s.language,
            periodStart = runCatching { LocalDate.parse(s.periodStart) }.getOrNull() ?: LocalDate.of(2026, 9, 1),
            weekCount = if (s.weekCount > 0) s.weekCount else 2,
            periodTitle = s.periodTitle,
            lastFetchedAt = s.lastFetchedAt,
            intersectionStrictness = s.intersectionStrictness,
            alwaysShowAllTrafficLights = s.alwaysShowAllTrafficLights,
            notifyEnabled = s.notifyEnabled,
            notifyTime1 = s.notifyTime1,
            notifyTime2 = s.notifyTime2,
            theme = s.theme,
            animations = s.animations,
            useUniversityXml = s.useUniversityXml
        )
    }

    private fun LessonEntity.toLesson() = Lesson(
        groupId = groupId, dayOfWeek = dayOfWeek, parity = parity, index = idx,
        timeStart = timeStart, timeEnd = timeEnd, subjectRaw = subjectRaw,
        subjectNormalized = subjectNormalized, teacherRaw = teacherRaw,
        roomRaw = roomRaw, buildingRaw = buildingRaw, typeRaw = typeRaw,
        classroomRaw = classroomRaw
    )

    private fun Lesson.toEntity() = LessonEntity(
        groupId = groupId, dayOfWeek = dayOfWeek, parity = parity, idx = index,
        timeStart = timeStart, timeEnd = timeEnd, subjectRaw = subjectRaw,
        subjectNormalized = subjectNormalized, teacherRaw = teacherRaw,
        roomRaw = roomRaw, buildingRaw = buildingRaw, typeRaw = typeRaw,
        classroomRaw = classroomRaw
    )
}
