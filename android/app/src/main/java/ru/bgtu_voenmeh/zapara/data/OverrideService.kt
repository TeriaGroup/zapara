package ru.bgtu_voenmeh.zapara.data

import ru.bgtu_voenmeh.zapara.data.db.OverrideDao
import ru.bgtu_voenmeh.zapara.data.db.OverrideEntity
import ru.bgtu_voenmeh.zapara.data.sync.OverrideValue
import ru.bgtu_voenmeh.zapara.data.sync.RoomSyncOutbox
import ru.bgtu_voenmeh.zapara.data.sync.SyncLocalIdentity
import ru.bgtu_voenmeh.zapara.data.sync.SyncValidation
import java.time.LocalDate
import java.util.UUID

// Port of Vograph.Core OverrideService. Global scope wins over weekday scope.
class OverrideService(
    private val dao: OverrideDao,
    private val outbox: RoomSyncOutbox? = null
) {

    fun addOrUpdate(subjectRaw: String, scope: String, displayName: String, note: String?): Long {
        if (outbox?.enabled == true) {
            return outbox.inTransaction { addOrUpdateCore(subjectRaw, scope, displayName, note, enqueue = true) }
        }
        return addOrUpdateCore(subjectRaw, scope, displayName, note, enqueue = false)
    }

    private fun addOrUpdateCore(
        subjectRaw: String,
        scope: String,
        displayName: String,
        note: String?,
        enqueue: Boolean
    ): Long {
        val norm = Parity.normalizeSubject(subjectRaw)
        val existing = dao.getAll().firstOrNull { it.subjectRawNormalized == norm && it.scope == scope }
        if (enqueue && existing != null) {
            val ident = ensureOverrideIdentity(existing)
            dao.deleteById(existing.id)
            dao.insert(existing.copy(displayName = displayName, note = note, id = existing.id))
            val value = OverrideValue(
                norm,
                SyncValidation.normalizeSubject(norm),
                scope,
                displayName,
                note,
                ident.createdAtUtc
            )
            outbox!!.enqueue(UUID.randomUUID(), "override", ident.entityId, ident.revision, "upsert", value, existing.id)
            return existing.id
        }
        if (existing != null) {
            dao.deleteByKey(norm, scope)
        }
        val id = dao.insert(
            OverrideEntity(
                subjectRawNormalized = norm, scope = scope,
                displayName = displayName, note = note,
                createdAt = LocalDate.now().toString()
            )
        )
        if (enqueue) {
            val raw = if (subjectRaw.isBlank()) subjectRaw else subjectRaw.trim()
            val utc = outbox!!.nowUtc()
            val entityId = UUID.randomUUID()
            outbox.remember("override", id, entityId, 0, utc, null)
            outbox.enqueue(
                UUID.randomUUID(),
                "override",
                entityId,
                0,
                "upsert",
                OverrideValue(raw, SyncValidation.normalizeSubject(raw), scope, displayName, note, utc),
                id
            )
        }
        return id
    }

    fun displayName(subjectRaw: String, dayOfWeek: Int): String {
        val found = displayNameByNorm(Parity.normalizeSubject(subjectRaw), dayOfWeek)
        return found.ifEmpty { subjectRaw }
    }

    fun displayNameByNorm(norm: String, dayOfWeek: Int): String {
        val all = dao.getAll().filter { it.subjectRawNormalized == norm }
        all.firstOrNull { it.scope == "global" }?.let { return it.displayName }
        all.firstOrNull { it.scope == "weekday:$dayOfWeek" }?.let { return it.displayName }
        return ""
    }

    fun note(subjectRaw: String, dayOfWeek: Int): String {
        return noteByNorm(Parity.normalizeSubject(subjectRaw), dayOfWeek)
    }

    fun noteByNorm(norm: String, dayOfWeek: Int): String {
        val all = dao.getAll().filter { it.subjectRawNormalized == norm }
        val ov = all.firstOrNull { it.scope == "global" }
            ?: all.firstOrNull { it.scope == "weekday:$dayOfWeek" }
        return ov?.note.orEmpty()
    }

    fun remove(id: Long) {
        if (outbox?.enabled == true) {
            outbox.inTransaction {
                val existing = dao.getAll().firstOrNull { it.id == id } ?: return@inTransaction 0
                val ident = ensureOverrideIdentity(existing)
                dao.deleteById(id)
                outbox.enqueue(UUID.randomUUID(), "override", ident.entityId, ident.revision, "delete", null, id)
                0
            }
            return
        }
        dao.deleteById(id)
    }

    private fun ensureOverrideIdentity(existing: OverrideEntity): SyncLocalIdentity {
        outbox!!.identity("override", existing.id)?.let { return it }
        val utc = outbox.nowUtc()
        return outbox.remember("override", existing.id, UUID.randomUUID(), 0, utc, null)
    }

    fun all(): List<OverrideEntity> = dao.getAll()
}
