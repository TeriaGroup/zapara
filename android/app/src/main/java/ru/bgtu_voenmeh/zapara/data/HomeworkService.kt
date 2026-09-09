package ru.bgtu_voenmeh.zapara.data

import ru.bgtu_voenmeh.zapara.data.db.HomeworkDao
import ru.bgtu_voenmeh.zapara.data.db.HomeworkEntity
import ru.bgtu_voenmeh.zapara.data.sync.CompletionValue
import ru.bgtu_voenmeh.zapara.data.sync.HomeworkValue
import ru.bgtu_voenmeh.zapara.data.sync.RoomSyncOutbox
import ru.bgtu_voenmeh.zapara.data.sync.SyncLocalIdentity
import ru.bgtu_voenmeh.zapara.data.sync.SyncValidation
import java.time.DayOfWeek
import java.time.LocalDate
import java.util.UUID

// Port of Vograph.Core HomeworkService. Statuses:
// far (hidden) / approaching (gray) / burning (due tomorrow) /
// burning_urgent (due today) / overdue / done.
data class SchedCtx(
    val groupId: String,
    val periodStart: LocalDate,
    val weekCount: Int,
    val invert: Boolean
)

data class Homework(
    val id: Long,
    val norm: String,
    val text: String,
    val createdAt: LocalDate,
    val n: Int,
    val due: LocalDate?,
    val status: String,
    val done: Boolean
)

class HomeworkService(
    private val dao: HomeworkDao,
    private val lessonsFor: (groupId: String, dow: Int, parity: Int) -> List<Lesson>,
    private val ctx: () -> SchedCtx?,
    private val outbox: RoomSyncOutbox? = null
) {

    fun addHomework(
        subjectRaw: String,
        text: String,
        n: Int,
        createdAt: LocalDate = LocalDate.now(),
        opId: UUID? = null
    ): Long {
        if (outbox?.enabled == true) {
            return outbox.inTransaction { addCore(subjectRaw, text, n, createdAt, opId, enqueue = true) }
        }
        return addCore(subjectRaw, text, n, createdAt, opId, enqueue = false)
    }

    private fun addCore(
        subjectRaw: String,
        text: String,
        n: Int,
        createdAt: LocalDate,
        opId: UUID?,
        enqueue: Boolean
    ): Long {
        val nth = n.coerceIn(1, 10)
        val idOp = opId ?: UUID.randomUUID()
        val raw = if (subjectRaw.isBlank()) subjectRaw else subjectRaw.trim()
        val (utc, legacy) = if (enqueue) outbox!!.splitCreated(createdAt) else null to createdAt
        val value = if (enqueue) {
            HomeworkValue(raw, SyncValidation.normalizeSubject(raw), text, nth, utc!!, legacy)
        } else {
            null
        }
        if (enqueue && outbox!!.find(idOp) != null) {
            val existing = outbox.find(idOp)!!
            if (!outbox.sameIntent(existing, "homework", "upsert", value)) {
                throw IllegalStateException("Повтор операции с другим содержимым.")
            }
            return existing.localRowId ?: 0
        }
        val norm = Parity.normalizeSubject(subjectRaw)
        val due = computeDueDate(norm, createdAt, nth)
        val status = computeStatus(norm, createdAt, nth, due, false)
        val id = dao.insert(
            HomeworkEntity(
                subjectRawNormalized = norm, text = text,
                createdAt = createdAt.toString(), targetNthOccurrence = nth,
                dueDateComputed = due?.toString(), status = status
            )
        )
        if (enqueue) {
            outbox!!.remember("homework", id, UUID.randomUUID(), 0, utc!!, legacy)
            val ident = outbox.identity("homework", id)!!
            outbox.enqueue(idOp, "homework", ident.entityId, 0, "upsert", value, id)
        }
        return id
    }

    fun updateHomework(id: Long, text: String, n: Int, opId: UUID? = null) {
        if (outbox?.enabled == true) {
            outbox.inTransaction { updateCore(id, text, n, opId, enqueue = true); 0 }
            return
        }
        updateCore(id, text, n, opId, enqueue = false)
    }

    private fun updateCore(id: Long, text: String, n: Int, opId: UUID?, enqueue: Boolean) {
        val e = dao.getById(id) ?: return
        val nth = n.coerceIn(1, 10)
        val createdAt = LocalDate.parse(e.createdAt)
        val due = computeDueDate(e.subjectRawNormalized, createdAt, nth)
        val status = computeStatus(e.subjectRawNormalized, createdAt, nth, due, false)
        dao.update(e.copy(text = text, targetNthOccurrence = nth, dueDateComputed = due?.toString(), status = status))
        if (enqueue) {
            val ident = ensureHomeworkIdentity(id, createdAt)
            val value = HomeworkValue(
                e.subjectRawNormalized,
                SyncValidation.normalizeSubject(e.subjectRawNormalized),
                text,
                nth,
                ident.createdAtUtc,
                ident.legacyCreatedLocalDate
            )
            outbox!!.enqueue(opId ?: UUID.randomUUID(), "homework", ident.entityId, ident.revision, "upsert", value, id)
        }
    }

    fun markDone(id: Long, done: Boolean) {
        if (outbox?.enabled == true) {
            outbox.inTransaction { markDoneCore(id, done, enqueue = true); 0 }
            return
        }
        markDoneCore(id, done, enqueue = false)
    }

    private fun markDoneCore(id: Long, done: Boolean, enqueue: Boolean) {
        val e = dao.getById(id) ?: return
        if (done) {
            dao.update(e.copy(status = "done", doneAt = LocalDate.now().toString()))
        } else {
            dao.update(e.copy(status = "pending", doneAt = null))
        }
        if (enqueue) {
            val ident = ensureHomeworkIdentity(id, LocalDate.parse(e.createdAt))
            val completionRev = outbox!!.identity("completion", id)?.revision ?: 0L
            val doneAt = if (done) outbox.nowUtc() else null
            outbox.remember("completion", id, ident.entityId, completionRev, ident.createdAtUtc, ident.legacyCreatedLocalDate)
            outbox.enqueue(
                UUID.randomUUID(),
                "completion",
                ident.entityId,
                completionRev,
                "upsert",
                CompletionValue(done, doneAt),
                id
            )
        }
        if (!done) recomputeAll()
    }

    fun delete(id: Long, opId: UUID? = null) {
        if (outbox?.enabled == true) {
            outbox.inTransaction { deleteCore(id, opId, enqueue = true); 0 }
            return
        }
        deleteCore(id, opId, enqueue = false)
    }

    private fun deleteCore(id: Long, opId: UUID?, enqueue: Boolean) {
        if (!enqueue) {
            dao.deleteById(id)
            return
        }
        val e = dao.getById(id) ?: return
        val ident = ensureHomeworkIdentity(id, LocalDate.parse(e.createdAt))
        val entityId = ident.entityId
        val completionRev = outbox!!.identity("completion", id)?.revision ?: 0L
        outbox.cancelPending("completion", entityId)
        if (completionRev > 0) {
            outbox.enqueue(UUID.randomUUID(), "completion", entityId, completionRev, "delete", null, id)
        }
        dao.deleteById(id)
        outbox.enqueue(opId ?: UUID.randomUUID(), "homework", entityId, ident.revision, "delete", null, id)
    }

    fun getById(id: Long): Homework? = dao.getById(id)?.toHomework()

    private fun ensureHomeworkIdentity(id: Long, createdAt: LocalDate): SyncLocalIdentity {
        outbox!!.identity("homework", id)?.let { return it }
        val (utc, legacy) = outbox.splitCreated(createdAt)
        return outbox.remember("homework", id, UUID.randomUUID(), 0, utc, legacy)
    }

    fun forSubject(subjectRaw: String): List<Homework> {
        return forSubjectByNorm(Parity.normalizeSubject(subjectRaw))
    }

    fun forSubjectByNorm(norm: String): List<Homework> {
        return dao.getAll().filter { it.subjectRawNormalized == norm }.map { it.toHomework() }
    }

    /** Existing persisted guest homework; callers must use IO. */
    fun all(): List<Homework> = dao.getAll().map { it.toHomework() }

    fun computeDueDate(norm: String, from: LocalDate, n: Int): LocalDate? {
        val c = ctx() ?: return null
        return dueDateIn(lessonsFor, c, norm, from, n)
    }

    /**
     * Due computation over an explicit lessons provider — pass an in-memory list
     * wrapper for main-thread-safe previews (Room forbids main-thread queries).
     */
    fun dueDateIn(
        lessons: (groupId: String, dow: Int, parity: Int) -> List<Lesson>,
        c: SchedCtx,
        norm: String,
        from: LocalDate,
        n: Int
    ): LocalDate? {
        var found = 0
        for (offset in 1..120) {
            val date = from.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val dow = date.dayOfWeek.value
            var code = Parity.weekCode(date, c.periodStart, c.weekCount)
            if (c.invert) code = if (code == 1) 2 else 1
            val dayLessons = lessons(c.groupId, dow, code)
            for (l in dayLessons) {
                if (l.subjectNormalized == norm) {
                    found++
                    if (found == n) return date
                    break // one count per day (mirrors Windows)
                }
            }
        }
        return null
    }

    fun computeStatus(norm: String, createdAt: LocalDate, n: Int, due: LocalDate?, done: Boolean, today: LocalDate = LocalDate.now()): String {
        if (done) return "done"
        if (due == null) return "pending"
        val c = ctx() ?: return "pending"
        val daysDiff = due.toEpochDay() - today.toEpochDay()
        if (daysDiff < 0) return "overdue"
        if (daysDiff == 0L) return "burning_urgent"
        if (daysDiff == 1L) return "burning"
        var before = 0
        var d = today.plusDays(1)
        var guard = 0
        while (d.isBefore(due) && guard++ < 130) {
            if (d.dayOfWeek != DayOfWeek.SUNDAY) {
                val dow = d.dayOfWeek.value
                var code = Parity.weekCode(d, c.periodStart, c.weekCount)
                if (c.invert) code = if (code == 1) 2 else 1
                before += lessonsFor(c.groupId, dow, code).count { it.subjectNormalized == norm }
            }
            d = d.plusDays(1)
        }
        if (before == 1) return "approaching"
        if (before == 0 && daysDiff <= 3) return "approaching"
        return "far"
    }

    fun recomputeAll(today: LocalDate = LocalDate.now()) {
        for (e in dao.getAll()) {
            if (e.status == "done") continue
            val due = computeDueDate(e.subjectRawNormalized, LocalDate.parse(e.createdAt), e.targetNthOccurrence)
            val status = computeStatus(e.subjectRawNormalized, LocalDate.parse(e.createdAt), e.targetNthOccurrence, due, false, today)
            if (due?.toString() != e.dueDateComputed || status != e.status) {
                dao.update(e.copy(dueDateComputed = due?.toString(), status = status))
            }
        }
    }

    private fun HomeworkEntity.toHomework() = Homework(
        id = id, norm = subjectRawNormalized, text = text,
        createdAt = LocalDate.parse(createdAt), n = targetNthOccurrence,
        due = dueDateComputed?.let { LocalDate.parse(it) },
        status = status, done = status == "done"
    )
}
