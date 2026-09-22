package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.db.SyncOutboxEntity
import java.util.UUID

/** Projection runs inside the same Room transaction as the durable cursor and inbox. */
interface SyncRecordProjection {
    fun apply(record: SyncRecord, outbox: RoomSyncOutbox): Long?
    fun localHomework(entityId: UUID, outbox: RoomSyncOutbox): HomeworkValue? = null
    fun localCompletion(entityId: UUID, outbox: RoomSyncOutbox): CompletionValue? = null
}

/** Reserved journal rows share the profile's existing Room transaction; no schema wipe is needed. */
class RoomSyncInbox internal constructor(
    private val outbox: RoomSyncOutbox,
    private val projection: SyncRecordProjection?
) {
    private val rows get() = outbox.commands
    val initialized: Boolean get() = rows.find(READY)?.syncEpoch == outbox.syncEpoch?.toString() && outbox.syncEpoch != null
    val manifest: SyncResyncManifest? get() = rows.find(MANIFEST)?.payload?.let { SyncJson.resyncManifest(it) }
    val afterOrdinal: Long get() = rows.find(MANIFEST)?.expectedRevision ?: 0

    fun requireSnapshot() { rows.delete(READY) }

    fun begin(manifest: SyncResyncManifest) = outbox.inTransaction {
        clearStage()
        rows.upsert(journal(MANIFEST, "manifest", manifest.manifestId, SyncJson.serialize(manifest))
            .copy(syncEpoch = manifest.syncEpoch.toString()))
    }

    fun discardManifest() = outbox.inTransaction { clearStage() }

    fun stage(page: SyncResyncPage) = outbox.inTransaction {
        check(manifest == page.manifest && afterOrdinal == page.afterOrdinal) { "Снимок синхронизации изменился." }
        for (item in page.items) {
            val key = stageKey(item.record)
            check(rows.find(key) == null) { "Повтор записи в снимке." }
            rows.upsert(journal(key, "stage", item.record.entityId, SyncJson.serialize(item.record))
                .copy(entityType = item.record.entityType, expectedRevision = item.ordinal))
        }
        rows.upsert(rows.find(MANIFEST)!!.copy(expectedRevision = page.nextAfterOrdinal))
    }

    fun publish(checkCurrent: () -> Unit = {}): Boolean = outbox.inTransaction {
        checkCurrent()
        val snapshot = checkNotNull(manifest)
        val changedEpoch = outbox.syncEpoch != null && outbox.syncEpoch != snapshot.syncEpoch
        check(afterOrdinal == snapshot.itemCount)
        val staged = rows.pending().filter { it.status == "stage" }.map { SyncJson.record(it.payload!!) }
        check(staged.size.toLong() == snapshot.itemCount)
        val keys = staged.map { serverKey(it.entityType, it.entityId) }.toSet()
        val oldIdentities = rows.pending().filter { it.status == RoomSyncOutbox.IDENTITY_STATUS && it.expectedRevision > 0 }
        rows.pending().filter { it.status == "server" }.forEach { rows.delete(it.opId) }
        // A server reset can omit records whose tombstones have expired. Remove only known
        // synced projections; local pending edits are retained as conflicts, never discarded.
        for (identity in oldIdentities) {
            val id = UUID.fromString(identity.entityId)
            if (serverKey(identity.entityType, id) !in keys) {
                applyRecord(SyncRecord(identity.entityType, id, maxOf(1, snapshot.highWater), true, snapshot.createdAt, null), force = true, reset = true)
            }
        }
        // Homework must exist before its completion is projected.
        staged.sortedBy { if (it.entityType == "completion") 1 else 0 }.forEach { applyRecord(it, force = true, reset = changedEpoch) }
        outbox.setEpoch(snapshot.syncEpoch, snapshot.highWater)
        rows.upsert(journal(READY, "ready", snapshot.syncEpoch, null).copy(syncEpoch = snapshot.syncEpoch.toString()))
        clearStage()
        checkCurrent()
        true
    }

    fun applyChanges(page: SyncChangesPage, checkCurrent: () -> Unit = {}): Boolean = outbox.inTransaction {
        checkCurrent()
        check(initialized && page.metadata.syncEpoch == outbox.syncEpoch && page.afterSequence == outbox.afterSequence)
        page.changes.forEach { change ->
            val submitted = outbox.find(change.opId)
            if (submitted != null && submitted.syncEpoch == page.metadata.syncEpoch &&
                submitted.entityType == change.record.entityType && submitted.entityId == change.record.entityId &&
                submitted.expectedRevision < change.record.revision &&
                (submitted.action == "delete") == change.record.tombstone && outbox.payloadValue(submitted) == change.record.value) {
                outbox.applyAck(submitted, change.record)
            }
            applyRecord(change.record)
        }
        outbox.setEpoch(page.metadata.syncEpoch, page.nextAfterSequence)
        checkCurrent()
        page.changes.isNotEmpty()
    }

    fun serverRecord(type: String, id: UUID): SyncRecord? =
        rows.find(serverKey(type, id))?.payload?.let { SyncJson.record(it) }

    fun conflicts(): List<SyncConflict> = outbox.inTransaction {
        outbox.pending().groupBy { it.entityType to it.entityId }.values
            .filter { group -> group.any { it.status == "conflict" } }
            .map { group ->
                val operation = group.maxWith(compareBy<PrivateSyncOutboxEntry> { it.createdAtUtc }.thenBy { it.opId })
                val server = serverRecord(operation.entityType, operation.entityId)
                val parent = if (operation.entityType == "completion") serverRecord("homework", operation.entityId) else null
                SyncConflict(operation, outbox.payloadValue(operation), server, parent, outbox.syncEpoch,
                    !(operation.entityType == "settings" && server?.tombstone == true) &&
                        !(operation.entityType == "completion" && (parent == null || parent.tombstone) &&
                            projection?.localHomework(operation.entityId, outbox) == null))
            }
    }

    fun resolve(shown: SyncConflict, keepLocal: Boolean): Boolean = outbox.inTransaction {
        val current = conflicts().firstOrNull { it.operation.entityType == shown.operation.entityType && it.operation.entityId == shown.operation.entityId }
        if (current != shown || (keepLocal && !shown.canKeepLocal)) return@inTransaction false
        val operation = shown.operation
        val type = operation.entityType
        val id = operation.entityId
        val server = shown.serverRecord
        val localRow = operation.localRowId ?: outbox.localRow(type, id)
        fun removeOperations(entityType: String) = outbox.pending().filter { it.entityType == entityType && it.entityId == id }
            .forEach { rows.delete(it.opId.toString()) }
        if (!keepLocal || (operation.action == "delete" && (server == null || server.tombstone))) {
            removeOperations(type)
            if (server != null) applyRecord(server, force = true)
            else {
                projection?.apply(SyncRecord(type, id, maxOf(1, outbox.afterSequence), true, outbox.nowUtc(), null), outbox)
                localRow?.let { rows.delete(RoomSyncOutbox.identityOp(type, it)) }
            }
            if (type == "completion" && shown.parentRecord?.tombstone == true &&
                outbox.pending().none { it.entityType == "homework" && it.entityId == id }) {
                applyRecord(shown.parentRecord, force = true)
            }
            return@inTransaction true
        }

        val recreateHomework = (type == "homework" && server?.tombstone == true) ||
            (type == "completion" && (server?.tombstone == true || shown.parentRecord == null || shown.parentRecord.tombstone))
        if (recreateHomework) {
            val homework = if (type == "homework") shown.localValue as? HomeworkValue else projection?.localHomework(id, outbox)
            checkNotNull(homework) { "Локальное задание недоступно." }
            val homeworkRow = outbox.localRow("homework", id) ?: localRow
            val completion = if (type == "completion") shown.localValue as? CompletionValue else projection?.localCompletion(id, outbox)
            removeOperations("homework")
            removeOperations("completion")
            val freshId = UUID.randomUUID()
            homeworkRow?.let {
                outbox.remember("homework", it, freshId, 0, homework.createdAtUtc, homework.legacyCreatedLocalDate)
                outbox.remember("completion", it, freshId, 0, homework.createdAtUtc, homework.legacyCreatedLocalDate)
            }
            projection?.apply(SyncRecord("homework", freshId, 1, false, outbox.nowUtc(), homework), outbox)
            outbox.enqueue(UUID.randomUUID(), "homework", freshId, 0, "upsert", homework, homeworkRow)
            if (completion != null) {
                projection?.apply(SyncRecord("completion", freshId, 1, false, outbox.nowUtc(), completion), outbox)
                outbox.enqueue(UUID.randomUUID(), "completion", freshId, 0, "upsert", completion, homeworkRow)
            }
            return@inTransaction true
        }

        val targetId = if (server?.tombstone == true) UUID.randomUUID() else id
        val baseRevision = if (server == null || server.tombstone) 0 else server.revision
        val value = when {
            shown.localValue is HomeworkValue && server?.value is HomeworkValue -> shown.localValue.copy(
                createdAtUtc = server.value.createdAtUtc, legacyCreatedLocalDate = server.value.legacyCreatedLocalDate)
            shown.localValue is OverrideValue && server?.value is OverrideValue -> shown.localValue.copy(createdAtUtc = server.value.createdAtUtc)
            else -> shown.localValue
        }
        removeOperations(type)
        localRow?.let {
            val old = outbox.identity(type, it)
            outbox.remember(type, it, targetId, baseRevision,
                when (value) { is HomeworkValue -> value.createdAtUtc; is OverrideValue -> value.createdAtUtc; else -> old?.createdAtUtc ?: outbox.nowUtc() },
                (value as? HomeworkValue)?.legacyCreatedLocalDate)
        }
        projection?.apply(SyncRecord(type, targetId, maxOf(1, baseRevision), operation.action == "delete", outbox.nowUtc(), value), outbox)
        outbox.enqueue(UUID.randomUUID(), type, targetId, baseRevision, operation.action, value, localRow)
        true
    }

    internal fun forgetServer(type: String, id: UUID) { rows.delete(serverKey(type, id)) }

    internal fun rememberServer(record: SyncRecord) {
        val previous = serverRecord(record.entityType, record.entityId)
        if (previous != null && previous.revision > record.revision) return
        rows.upsert(journal(serverKey(record.entityType, record.entityId), "server", record.entityId, SyncJson.serialize(record))
            .copy(entityType = record.entityType, expectedRevision = record.revision))
    }

    private fun applyRecord(record: SyncRecord, force: Boolean = false, reset: Boolean = false) {
        val previous = serverRecord(record.entityType, record.entityId)
        if (!force && previous != null && previous.revision >= record.revision) return
        val pending = outbox.pending().filter {
            it.entityId == record.entityId && (it.entityType == record.entityType ||
                (record.entityType == "homework" && record.tombstone && it.entityType == "completion"))
        }
        rememberServer(record)
        if (pending.isNotEmpty()) {
            val awaitingReceipt = !reset && pending.any {
                it.syncEpoch != null && it.expectedRevision < record.revision &&
                    (it.action == "delete") == record.tombstone && outbox.payloadValue(it) == record.value
            }
            if (awaitingReceipt) return
            pending.filter { reset || it.expectedRevision != record.revision }.forEach {
                rows.find(it.opId.toString())?.let { row -> rows.upsert(row.copy(status = "conflict")) }
            }
            return
        }
        val local = projection?.apply(record, outbox)
        if (local != null) {
            val value = record.value
            val old = outbox.identity(record.entityType, local)
            outbox.remember(record.entityType, local, record.entityId, record.revision,
                when (value) { is HomeworkValue -> value.createdAtUtc; is OverrideValue -> value.createdAtUtc; else -> old?.createdAtUtc ?: record.changedAt },
                if (value is HomeworkValue) value.legacyCreatedLocalDate else old?.legacyCreatedLocalDate)
        }
        if (record.entityType == "homework" && !record.tombstone &&
            outbox.pending().none { it.entityType == "completion" && it.entityId == record.entityId }) {
            serverRecord("completion", record.entityId)?.let { completion ->
                projection?.apply(completion, outbox)?.let { id ->
                    val homework = outbox.identity("homework", id)!!
                    outbox.remember("completion", id, completion.entityId, completion.revision,
                        homework.createdAtUtc, homework.legacyCreatedLocalDate)
                }
            }
        }
    }

    private fun clearStage() {
        rows.pending().filter { it.status == "stage" || it.opId == MANIFEST }.forEach { rows.delete(it.opId) }
    }

    private fun journal(key: String, status: String, id: UUID, payload: ByteArray?) =
        SyncOutboxEntity(key, "journal", id.toString(), 0, "journal", payload, null, status, outbox.nowUtc().toString())

    companion object {
        private const val READY = "receive:ready"
        private const val MANIFEST = "receive:manifest"
        private fun serverKey(type: String, id: UUID) = "receive:server:$type:$id"
        private fun stageKey(record: SyncRecord) = "receive:stage:${record.entityType}:${record.entityId}"
    }
}
