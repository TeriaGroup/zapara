package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.db.SyncOutboxEntity
import ru.bgtu_voenmeh.zapara.data.db.SyncStateEntity
import java.time.Clock
import java.time.Instant
import java.util.UUID

data class PrivateSyncOutboxEntry(
    val opId: UUID,
    val entityType: String,
    val entityId: UUID,
    val expectedRevision: Long,
    val action: String,
    val status: String,
    val localRowId: Long?,
    val syncEpoch: UUID?,
    val createdAtUtc: Instant
)

data class PrivateSyncDraft(
    val entityType: String,
    val entityId: UUID,
    val opId: UUID?,
    val localPayload: String,
    val serverPayload: String
)

/** Room-shaped commands a later task can implement against [SyncOutboxEntity]. */
interface SyncOutboxCommands {
    fun pending(): List<SyncOutboxEntity>
    fun find(opId: String): SyncOutboxEntity?
    fun upsert(row: SyncOutboxEntity)
    fun delete(opId: String)
    fun clearPendingEpochs()
}

interface SyncStateCommands {
    fun get(): SyncStateEntity?
    fun upsert(row: SyncStateEntity)
}

class MemorySyncOutboxCommands : SyncOutboxCommands {
    private val rows = LinkedHashMap<String, SyncOutboxEntity>()
    override fun pending(): List<SyncOutboxEntity> = rows.values.toList()
    override fun find(opId: String): SyncOutboxEntity? = rows[opId]
    override fun upsert(row: SyncOutboxEntity) { rows[row.opId] = row }
    override fun delete(opId: String) { rows.remove(opId) }
    override fun clearPendingEpochs() {
        val snapshot = rows.values.toList()
        for (row in snapshot) {
            if (row.status == "pending") rows[row.opId] = row.copy(syncEpoch = null)
        }
    }
}

class MemorySyncStateCommands : SyncStateCommands {
    private var row: SyncStateEntity? = SyncStateEntity(1, null, 0)
    override fun get(): SyncStateEntity? = row
    override fun upsert(row: SyncStateEntity) { this.row = row }
}

class MemoryPrivateSyncOutbox(
    val enabled: Boolean = true,
    private val clock: Clock = Clock.systemUTC()
) {
    private val rows = LinkedHashMap<UUID, PrivateSyncOutboxEntry>()
    private val payloads = HashMap<UUID, ByteArray?>()
    private val draftRows = LinkedHashMap<Pair<String, UUID>, PrivateSyncDraft>()
    var syncEpoch: UUID? = null
        private set
    var afterSequence: Long = 0
        private set

    fun pending(): List<PrivateSyncOutboxEntry> =
        rows.values.sortedWith(compareBy({ it.createdAtUtc }, { it.opId }))

    fun find(opId: UUID): PrivateSyncOutboxEntry? = rows[opId]

    fun drafts(): List<PrivateSyncDraft> = draftRows.values.toList()

    fun payloadValue(row: PrivateSyncOutboxEntry): SyncValue? {
        val stored = payloads[row.opId] ?: return null
        return SyncJson.parseValue(row.entityType, stored)
    }

    fun setEpoch(epoch: UUID, afterSequence: Long) {
        val previous = syncEpoch
        syncEpoch = epoch
        this.afterSequence = afterSequence
        if (previous != epoch) clearRowEpochs()
    }

    fun clearRowEpochs() {
        for (row in rows.values.toList()) {
            if (row.status == "pending") rows[row.opId] = row.copy(syncEpoch = null)
        }
    }

    fun enqueue(
        opId: UUID,
        entityType: String,
        entityId: UUID,
        expectedRevision: Long,
        action: String,
        value: SyncValue?,
        localRowId: Long?
    ) {
        if (!enabled) return
        val existing = find(opId)
        if (existing != null) {
            if (!sameIntent(existing, entityType, action, value)) {
                throw IllegalStateException("Повтор операции с другим содержимым.")
            }
            return
        }
        var revision = expectedRevision
        for (row in pending().filter { it.entityType == entityType && it.entityId == entityId && it.status == "pending" }) {
            if (action == "delete" && row.expectedRevision == 0L && row.action == "upsert") {
                deleteOp(row.opId)
                return
            }
            revision = row.expectedRevision
            deleteOp(row.opId)
        }
        if (action == "delete" && revision == 0L) return
        val payload = value?.let { SyncJson.valueUtf8(it) }
        rows[opId] = PrivateSyncOutboxEntry(
            opId, entityType, entityId, revision, action, "pending", localRowId, null, Instant.now(clock)
        )
        payloads[opId] = payload
    }

    fun sameIntent(existing: PrivateSyncOutboxEntry, entityType: String, action: String, value: SyncValue?): Boolean {
        if (existing.entityType != entityType || existing.action != action) return false
        val stored = payloads[existing.opId]
        if (action == "delete") return value == null && stored == null
        if (value == null || stored == null) return false
        val proposed = SyncJson.valueUtf8(value)
        if (stored.contentEquals(proposed)) return true
        return when {
            entityType == "homework" && value is HomeworkValue -> {
                val parsed = SyncJson.parseValue(entityType, stored) as HomeworkValue
                parsed.text == value.text && parsed.subjectKey == value.subjectKey &&
                    parsed.targetNthOccurrence == value.targetNthOccurrence
            }
            entityType == "override" && value is OverrideValue -> {
                val parsed = SyncJson.parseValue(entityType, stored) as OverrideValue
                parsed.displayName == value.displayName && parsed.note == value.note &&
                    parsed.scope == value.scope && parsed.subjectKey == value.subjectKey
            }
            entityType == "completion" && value is CompletionValue ->
                SyncJson.parseValue(entityType, stored) == value
            entityType == "friend" && value is FriendValue ->
                SyncJson.parseValue(entityType, stored) == value
            entityType == "settings" && value is SettingsValue ->
                SyncJson.parseValue(entityType, stored) == value
            else -> false
        }
    }

    fun buildMutation(row: PrivateSyncOutboxEntry, epoch: UUID): SyncMutation {
        val storedEpoch = row.syncEpoch ?: stampEpoch(row.opId, epoch)
        val payload = payloads[row.opId]
        val value = payload?.let { bytes ->
            when (row.entityType) {
                "homework", "completion", "override", "friend", "settings" ->
                    SyncJson.parseValue(row.entityType, bytes)
                else -> throw IllegalStateException("Некорректный запрос синхронизации.")
            }
        }
        return SyncMutation(storedEpoch, row.opId, row.entityType, row.entityId, row.expectedRevision, row.action, value)
    }

    fun applyAck(row: PrivateSyncOutboxEntry, record: SyncRecord) {
        deleteOp(row.opId)
        draftRows.remove(row.entityType to row.entityId)
    }

    fun markConflict(row: PrivateSyncOutboxEntry, outcome: SyncMutationResult?) {
        val current = rows[row.opId] ?: return
        rows[row.opId] = current.copy(status = "conflict")
        val local = payloads[row.opId]
        val localText = if (local == null) "{}" else local.toString(Charsets.UTF_8)
        val serverText = outcome?.serverRecord?.let { SyncJson.serialize(it).toString(Charsets.UTF_8) } ?: "{}"
        draftRows[row.entityType to row.entityId] = PrivateSyncDraft(
            row.entityType, row.entityId, row.opId, localText, serverText
        )
    }

    fun hasPendingOrDraft(entityType: String, entityId: UUID): Boolean =
        pending().any { it.entityType == entityType && it.entityId == entityId } ||
            drafts().any { it.entityType == entityType && it.entityId == entityId }

    suspend fun pushPending(client: PrivateSyncHttpClient, access: String, epoch: UUID): PrivateSyncState {
        val snapshot = pending().filter { it.status == "pending" }
        var last = PrivateSyncState.Success
        for (row in snapshot) {
            val current = find(row.opId) ?: continue
            val mutation = buildMutation(current, epoch)
            val result = client.mutate(access, mutation)
            last = result.state
            when (result.state) {
                PrivateSyncState.Success -> {
                    val record = result.value?.serverRecord ?: result.mutationOutcome?.serverRecord
                    if (record != null) applyAck(find(row.opId) ?: current, record)
                }
                PrivateSyncState.Conflict -> markConflict(find(row.opId) ?: current, result.mutationOutcome)
                PrivateSyncState.ResetRequired -> {
                    clearRowEpochs()
                    return result.state
                }
                else -> return result.state
            }
        }
        return last
    }

    private fun stampEpoch(opId: UUID, epoch: UUID): UUID {
        val row = rows[opId] ?: return epoch
        if (row.syncEpoch != null) return row.syncEpoch
        rows[opId] = row.copy(syncEpoch = epoch)
        return epoch
    }

    private fun deleteOp(opId: UUID) {
        rows.remove(opId)
        payloads.remove(opId)
    }
}
