package ru.bgtu_voenmeh.zapara.data

import ru.bgtu_voenmeh.zapara.data.db.HomeworkDao
import ru.bgtu_voenmeh.zapara.data.db.HomeworkEntity
import java.time.DayOfWeek
import java.time.LocalDate

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
    private val ctx: () -> SchedCtx?
) {

    fun addHomework(subjectRaw: String, text: String, n: Int, createdAt: LocalDate = LocalDate.now()): Long {
        val norm = Parity.normalizeSubject(subjectRaw)
        val due = computeDueDate(norm, createdAt, n.coerceIn(1, 10))
        val status = computeStatus(norm, createdAt, n.coerceIn(1, 10), due, false)
        return dao.insert(
            HomeworkEntity(
                subjectRawNormalized = norm, text = text,
                createdAt = createdAt.toString(), targetNthOccurrence = n.coerceIn(1, 10),
                dueDateComputed = due?.toString(), status = status
            )
        )
    }

    fun updateHomework(id: Long, text: String, n: Int) {
        val e = dao.getById(id) ?: return
        val due = computeDueDate(e.subjectRawNormalized, LocalDate.parse(e.createdAt), n.coerceIn(1, 10))
        val status = computeStatus(e.subjectRawNormalized, LocalDate.parse(e.createdAt), n.coerceIn(1, 10), due, false)
        dao.update(e.copy(text = text, targetNthOccurrence = n.coerceIn(1, 10), dueDateComputed = due?.toString(), status = status))
    }

    fun markDone(id: Long, done: Boolean) {
        val e = dao.getById(id) ?: return
        if (done) {
            dao.update(e.copy(status = "done", doneAt = LocalDate.now().toString()))
        } else {
            dao.update(e.copy(status = "pending", doneAt = null))
            recomputeAll()
        }
    }

    fun delete(id: Long) {
        dao.deleteById(id)
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
