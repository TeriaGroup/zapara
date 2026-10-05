package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.db.*
import java.time.ZoneId

/** Writes local views directly, bypassing services which enqueue local mutations. */
class RoomSyncProjection(
    private val homework: HomeworkDao,
    private val overrides: OverrideDao,
    private val friends: FriendDao,
    private val settings: SettingsDao
) : SyncRecordProjection {
    override fun localHomework(entityId: java.util.UUID, outbox: RoomSyncOutbox): HomeworkValue? {
        val id = outbox.localRow("homework", entityId) ?: return null
        val row = homework.getById(id) ?: return null
        val identity = outbox.identity("homework", id) ?: return null
        return HomeworkValue(row.subjectRawNormalized, SyncValidation.normalizeSubject(row.subjectRawNormalized),
            row.text, row.targetNthOccurrence, identity.createdAtUtc, identity.legacyCreatedLocalDate)
    }

    override fun localCompletion(entityId: java.util.UUID, outbox: RoomSyncOutbox): CompletionValue? {
        val id = outbox.localRow("homework", entityId) ?: return null
        val row = homework.getById(id) ?: return null
        return CompletionValue(row.status == "done", if (row.status == "done") outbox.nowUtc() else null)
    }

    override fun apply(record: SyncRecord, outbox: RoomSyncOutbox): Long? {
        val id = outbox.localRow(record.entityType, record.entityId)
        when (record.entityType) {
            "homework" -> {
                if (record.tombstone) { id?.let { homework.deleteById(it) }; return id }
                val value = record.value as HomeworkValue
                val old = id?.let { homework.getById(it) }
                val date = value.legacyCreatedLocalDate ?: value.createdAtUtc.atZone(ZoneId.of("Europe/Moscow")).toLocalDate()
                val row = HomeworkEntity(id = id ?: 0, subjectRawNormalized = Parity.normalizeSubject(value.subjectRaw),
                    text = value.text, createdAt = date.toString(), targetNthOccurrence = value.targetNthOccurrence,
                    status = if (old?.status == "done") "done" else "pending", doneAt = old?.doneAt)
                if (old != null) { homework.update(row); return old.id }
                return homework.insert(row)
            }
            "completion" -> {
                val homeworkId = outbox.localRow("homework", record.entityId) ?: return null
                val old = homework.getById(homeworkId) ?: return null
                val value = record.value as? CompletionValue
                homework.update(old.copy(status = if (value?.done == true) "done" else "pending",
                    doneAt = value?.doneAtUtc?.toString()))
                return homeworkId
            }
            "override" -> {
                if (record.tombstone) { id?.let { overrides.deleteById(it) }; return id }
                val value = record.value as OverrideValue
                val row = OverrideEntity(id = id ?: 0, subjectRawNormalized = Parity.normalizeSubject(value.subjectRaw),
                    scope = value.scope, displayName = value.displayName, note = value.note,
                    createdAt = value.createdAtUtc.atZone(ZoneId.of("Europe/Moscow")).toLocalDate().toString())
                id?.let { overrides.deleteById(it) }
                return overrides.insert(row)
            }
            "friend" -> {
                if (record.tombstone) { id?.let { friends.delete(it) }; return id }
                val value = record.value as FriendValue
                val row = FriendEntity(id = id ?: 0, groupName = value.groupName,
                    colorHex = PALETTE[value.paletteIndex - 1], enabled = value.enabled, memberNames = value.memberNames)
                if (id != null && friends.getAll().any { it.id == id }) { friends.update(row); return id }
                return friends.insert(row)
            }
            "settings" -> {
                val old = settings.get() ?: SettingsEntity()
                val value = record.value as? SettingsValue
                settings.save(old.copy(myGroupId = value?.selectedGroupId, parityInvert = value?.parityInvert ?: false,
                    notifyTime1 = value?.notifyTime1, notifyTime2 = value?.notifyTime2,
                    intersectionStrictness = value?.strictness ?: 25, alwaysShowAllTrafficLights = value?.alwaysShow ?: false))
                return 1
            }
        }
        error("Неизвестная запись синхронизации.")
    }

    private companion object {
        val PALETTE = listOf("#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C")
    }
}
