package ru.bgtu_voenmeh.zapara.data.api

import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import java.time.LocalDate

interface TimetableStore {
    var useApiCatalog: Boolean
    fun runInTransaction(block: () -> Unit)
    fun settings(): ScheduleRepository.SettingsState
    fun saveSettings(state: ScheduleRepository.SettingsState)
    fun groups(): List<GroupInfo>
    fun storedGroups(): List<GroupInfo>
    fun upsertGroup(group: GroupInfo)
    fun ensureGroup(id: String, name: String, url: String? = null)
    fun allLessons(groupId: String): List<Lesson>
    fun replaceLessons(groupId: String, lessons: List<Lesson>)
    fun clearCatalog()
    fun insertCatalog(groupId: String, name: String)
    fun catalogIds(): Set<String>
    fun readMetadata(groupId: String): CacheMetadata?
    fun writeMetadata(groupId: String, metadata: CacheMetadata)
    fun friends(): List<Friend>
    fun dump(): String
}

class MemoryTimetableStore : TimetableStore {
    override var useApiCatalog: Boolean = false
    var failOnInsertGroupId: String? = null
    var homeworkText: String? = null
    val friends = mutableListOf<Friend>()

    private var settingsState = ScheduleRepository.SettingsState()
    private val groupRows = linkedMapOf<String, GroupInfo>()
    private val lessonRows = linkedMapOf<String, List<Lesson>>()
    private val catalog = linkedMapOf<String, String>()
    private val metadata = linkedMapOf<String, CacheMetadata>()

    override fun runInTransaction(block: () -> Unit) {
        val snap = Snap(
            settingsState, LinkedHashMap(groupRows), LinkedHashMap(lessonRows),
            LinkedHashMap(catalog), LinkedHashMap(metadata), homeworkText, ArrayList(friends)
        )
        try {
            block()
        } catch (e: Throwable) {
            settingsState = snap.settings
            groupRows.clear(); groupRows.putAll(snap.groups)
            lessonRows.clear(); lessonRows.putAll(snap.lessons)
            catalog.clear(); catalog.putAll(snap.catalog)
            metadata.clear(); metadata.putAll(snap.metadata)
            homeworkText = snap.homework
            friends.clear(); friends.addAll(snap.friends)
            throw e
        }
    }

    override fun settings(): ScheduleRepository.SettingsState = overlaySettings(this, settingsState)

    override fun saveSettings(state: ScheduleRepository.SettingsState) {
        settingsState = state
    }

    override fun groups(): List<GroupInfo> {
        val catalogOn = useApiCatalog && readMetadata("") != null
        return if (catalogOn) {
            catalog.map { (id, name) -> groupRows[id]?.copy(name = name) ?: GroupInfo(id, name) }
                .sortedBy { it.name }
        } else groupRows.values.sortedBy { it.name }
    }

    override fun storedGroups(): List<GroupInfo> = groupRows.values.toList()

    override fun upsertGroup(group: GroupInfo) {
        groupRows[group.id] = group
        metadata.remove(group.id)
    }

    override fun ensureGroup(id: String, name: String, url: String?) {
        val current = groupRows[id]
        groupRows[id] = GroupInfo(id, name, url ?: current?.url.orEmpty())
    }

    override fun allLessons(groupId: String): List<Lesson> = lessonRows[groupId].orEmpty()

    override fun replaceLessons(groupId: String, lessons: List<Lesson>) {
        if (failOnInsertGroupId != null && lessons.any { it.groupId == failOnInsertGroupId }) {
            throw IllegalStateException("test")
        }
        lessonRows[groupId] = lessons
    }

    override fun clearCatalog() {
        catalog.clear()
    }

    override fun insertCatalog(groupId: String, name: String) {
        catalog[groupId] = name
    }

    override fun catalogIds(): Set<String> = catalog.keys.toSet()

    override fun readMetadata(groupId: String): CacheMetadata? = metadata[groupId]

    override fun writeMetadata(groupId: String, metadata: CacheMetadata) {
        this.metadata[groupId] = metadata
    }

    override fun friends(): List<Friend> = friends.toList()

    override fun dump(): String {
        val parts = ArrayList<String>()
        groupRows.toSortedMap().forEach { (id, g) -> parts += "g|$id|${g.name}|${g.url}" }
        lessonRows.toSortedMap().forEach { (id, list) ->
            list.forEach { l -> parts += "l|$id|${l.dayOfWeek}|${l.parity}|${l.index}|${l.subjectRaw}" }
        }
        parts += "s|${settings().myGroupId}|${settings().periodStart}|${settings().lastFetchedAt}|${settings().useUniversityXml}"
        catalog.toSortedMap().forEach { (id, name) -> parts += "c|$id|$name" }
        metadata.toSortedMap().forEach { (id, m) ->
            parts += "m|$id|${m.source}|${m.meta?.snapshotId}|${m.period.start}|${m.fetchedAt}|${m.sourceBase}"
        }
        return parts.joinToString("\n")
    }

    private data class Snap(
        val settings: ScheduleRepository.SettingsState,
        val groups: Map<String, GroupInfo>,
        val lessons: Map<String, List<Lesson>>,
        val catalog: Map<String, String>,
        val metadata: Map<String, CacheMetadata>,
        val homework: String?,
        val friends: List<Friend>
    )
}

fun overlaySettings(store: TimetableStore, raw: ScheduleRepository.SettingsState): ScheduleRepository.SettingsState {
    if (raw.useUniversityXml) return raw
    val selected = raw.myGroupId
    val meta = selected?.let { store.readMetadata(it) }
    return if (meta != null) {
        raw.copy(
            periodStart = meta.period.start,
            periodTitle = meta.period.title,
            weekCount = meta.period.weekCount,
            lastFetchedAt = meta.fetchedAt
        )
    } else if (store.useApiCatalog) {
        raw.copy(lastFetchedAt = null)
    } else raw
}
