package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.db.SyncOutboxDao
import ru.bgtu_voenmeh.zapara.data.db.SyncOutboxEntity
import ru.bgtu_voenmeh.zapara.data.db.SyncStateDao
import ru.bgtu_voenmeh.zapara.data.db.SyncStateEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import java.time.Clock
import java.time.Instant
import java.time.LocalDate
import java.time.temporal.ChronoUnit
import java.util.UUID
import java.util.concurrent.Callable

data class SyncLocalIdentity(
    val entityId: UUID,
    val revision: Long,
    val createdAtUtc: Instant,
    val legacyCreatedLocalDate: LocalDate?
)

class RoomSyncOutboxCommands(private val dao: SyncOutboxDao) : SyncOutboxCommands {
    override fun pending(): List<SyncOutboxEntity> = dao.listAll()
    override fun find(opId: String): SyncOutboxEntity? = dao.find(opId)
    override fun upsert(row: SyncOutboxEntity) = dao.upsert(row)
    override fun delete(opId: String) = dao.delete(opId)
    override fun clearPendingEpochs() = dao.clearPendingEpochs()
}

class RoomSyncStateCommands(private val dao: SyncStateDao) : SyncStateCommands {
    override fun get(): SyncStateEntity? = dao.get()
    override fun upsert(row: SyncStateEntity) = dao.upsert(row)
}

class RoomSyncOutbox(
    val enabled: Boolean,
    private val commands: SyncOutboxCommands,
    private val state: SyncStateCommands = MemorySyncStateCommands(),
    private val transactor: (() -> Any?) -> Any? = { it() },
    val clock: Clock = Clock.systemUTC()
) {
    var beforeCommit: (() -> Unit)? = null

    var syncEpoch: UUID? = parseUuid(state.get()?.syncEpoch)
        private set
    var afterSequence: Long = state.get()?.afterSequence ?: 0L
        private set

    fun <T> inTransaction(action: () -> T): T {
        if (!enabled) return action()
        @Suppress("UNCHECKED_CAST")
        return transactor {
            val result = action()
            beforeCommit?.invoke()
            result
        } as T
    }

    fun pending(): List<PrivateSyncOutboxEntry> =
        commands.pending()
            .filter { it.status != IDENTITY_STATUS }
            .map { it.toEntry() }
            .sortedWith(compareBy({ it.createdAtUtc }, { it.opId }))

    fun find(opId: UUID): PrivateSyncOutboxEntry? =
        commands.find(opId.toString())?.takeUnless { it.status == IDENTITY_STATUS }?.toEntry()

    fun payloadValue(row: PrivateSyncOutboxEntry): SyncValue? {
        val stored = commands.find(row.opId.toString())?.payload ?: return null
        return SyncJson.parseValue(row.entityType, stored)
    }

    fun identity(entityType: String, localRowId: Long): SyncLocalIdentity? {
        val row = commands.find(identityOp(entityType, localRowId)) ?: return null
        if (row.status != IDENTITY_STATUS) return null
        val (utc, legacy) = parseIdentityPayload(row)
        return SyncLocalIdentity(UUID.fromString(row.entityId), row.expectedRevision, utc, legacy)
    }

    fun remember(
        entityType: String,
        localRowId: Long,
        entityId: UUID,
        revision: Long,
        createdAtUtc: Instant,
        legacyCreatedLocalDate: LocalDate?
    ): SyncLocalIdentity {
        commands.upsert(
            SyncOutboxEntity(
                opId = identityOp(entityType, localRowId),
                entityType = entityType,
                entityId = entityId.toString(),
                expectedRevision = revision,
                action = IDENTITY_ACTION,
                payload = identityPayload(createdAtUtc, legacyCreatedLocalDate),
                localRowId = localRowId,
                status = IDENTITY_STATUS,
                createdAtUtc = createdAtUtc.toString(),
                syncEpoch = null
            )
        )
        return SyncLocalIdentity(entityId, revision, createdAtUtc, legacyCreatedLocalDate)
    }

    fun splitCreated(created: LocalDate): Pair<Instant, LocalDate> =
        Instant.now(clock).truncatedTo(ChronoUnit.SECONDS) to created

    fun nowUtc(): Instant = Instant.now(clock).truncatedTo(ChronoUnit.SECONDS)

    fun setEpoch(epoch: UUID, afterSequence: Long) {
        val previous = syncEpoch
        state.upsert(SyncStateEntity(1, epoch.toString(), afterSequence))
        syncEpoch = epoch
        this.afterSequence = afterSequence
        if (previous != epoch) clearRowEpochs()
    }

    fun clearRowEpochs() {
        commands.clearPendingEpochs()
    }

    /** 410 `sync_reset`: drop stamped epochs so remaining ops are not restamped with the expired epoch. */
    fun abortExpiredEpoch() {
        clearRowEpochs()
    }

    fun abortIfReset(state: PrivateSyncState): Boolean {
        if (state != PrivateSyncState.ResetRequired) return false
        abortExpiredEpoch()
        return true
    }

    fun cancelPending(entityType: String, entityId: UUID) {
        for (row in pending().filter { it.entityType == entityType && it.entityId == entityId && it.status == "pending" }) {
            deleteOp(row.opId)
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
        commands.upsert(
            SyncOutboxEntity(
                opId = opId.toString(),
                entityType = entityType,
                entityId = entityId.toString(),
                expectedRevision = revision,
                action = action,
                payload = payload,
                localRowId = localRowId,
                status = "pending",
                createdAtUtc = Instant.now(clock).toString(),
                syncEpoch = null
            )
        )
        if (localRowId != null) {
            val prev = identity(entityType, localRowId)
            remember(
                entityType,
                localRowId,
                entityId,
                prev?.revision ?: 0,
                prev?.createdAtUtc ?: Instant.now(clock).truncatedTo(ChronoUnit.SECONDS),
                prev?.legacyCreatedLocalDate
            )
        }
    }

    fun sameIntent(existing: PrivateSyncOutboxEntry, entityType: String, action: String, value: SyncValue?): Boolean {
        if (existing.entityType != entityType || existing.action != action) return false
        val stored = commands.find(existing.opId.toString())?.payload
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
        val payload = commands.find(row.opId.toString())?.payload
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
        val localId = row.localRowId ?: return
        val prev = identity(row.entityType, localId)
        remember(
            row.entityType,
            localId,
            record.entityId,
            record.revision,
            prev?.createdAtUtc ?: Instant.now(clock).truncatedTo(ChronoUnit.SECONDS),
            prev?.legacyCreatedLocalDate
        )
    }

    private fun stampEpoch(opId: UUID, epoch: UUID): UUID {
        val current = commands.find(opId.toString()) ?: return epoch
        if (current.syncEpoch != null) return UUID.fromString(current.syncEpoch)
        commands.upsert(current.copy(syncEpoch = epoch.toString()))
        return epoch
    }

    private fun deleteOp(opId: UUID) {
        commands.delete(opId.toString())
    }

    private fun SyncOutboxEntity.toEntry() = PrivateSyncOutboxEntry(
        opId = UUID.fromString(opId),
        entityType = entityType,
        entityId = UUID.fromString(entityId),
        expectedRevision = expectedRevision,
        action = action,
        status = status,
        localRowId = localRowId,
        syncEpoch = parseUuid(syncEpoch),
        createdAtUtc = Instant.parse(createdAtUtc)
    )

    companion object {
        const val IDENTITY_STATUS = "identity"
        const val IDENTITY_ACTION = "identity"

        fun identityOp(entityType: String, localRowId: Long): String = "identity:$entityType:$localRowId"

        fun from(db: ZaparaDatabase, enabled: Boolean, clock: Clock = Clock.systemUTC()): RoomSyncOutbox =
            RoomSyncOutbox(
                enabled = enabled,
                commands = RoomSyncOutboxCommands(db.syncOutboxDao()),
                state = RoomSyncStateCommands(db.syncStateDao()),
                transactor = { action -> db.runInTransaction(Callable { action() }) },
                clock = clock
            )

        private fun parseUuid(value: String?): UUID? =
            if (value.isNullOrEmpty()) null else UUID.fromString(value)

        private fun identityPayload(utc: Instant, legacy: LocalDate?): ByteArray {
            val text = utc.toString() + "|" + (legacy?.toString() ?: "")
            return text.toByteArray(Charsets.UTF_8)
        }

        private fun parseIdentityPayload(row: SyncOutboxEntity): Pair<Instant, LocalDate?> {
            val text = row.payload?.toString(Charsets.UTF_8)
            if (text.isNullOrEmpty()) return Instant.parse(row.createdAtUtc) to null
            val parts = text.split('|', limit = 2)
            val utc = runCatching { Instant.parse(parts[0]) }.getOrElse { Instant.parse(row.createdAtUtc) }
            val legacy = parts.getOrNull(1)?.takeIf { it.isNotEmpty() }?.let { LocalDate.parse(it) }
            return utc to legacy
        }
    }
}
