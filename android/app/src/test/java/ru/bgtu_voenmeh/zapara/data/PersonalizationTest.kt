package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.db.HomeworkDao
import ru.bgtu_voenmeh.zapara.data.db.HomeworkEntity
import ru.bgtu_voenmeh.zapara.data.db.OverrideDao
import ru.bgtu_voenmeh.zapara.data.db.OverrideEntity
import java.time.LocalDate
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import ru.bgtu_voenmeh.zapara.ui.homework.homeworkEditorDueFor
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.XmlCopy

class FakeOverrideDao : OverrideDao {
    val items = mutableListOf<OverrideEntity>()
    private var seq = 1L
    override fun getAll(): List<OverrideEntity> = items.toList()
    override fun insert(e: OverrideEntity): Long {
        val id = seq++
        items.add(e.copy(id = id))
        return id
    }
    override fun deleteByKey(norm: String, scope: String): Int {
        val n = items.count { it.subjectRawNormalized == norm && it.scope == scope }
        items.removeAll { it.subjectRawNormalized == norm && it.scope == scope }
        return n
    }
    override fun deleteById(id: Long): Int {
        val n = items.count { it.id == id }
        items.removeAll { it.id == id }
        return n
    }
}

class FakeHomeworkDao : HomeworkDao {
    val items = mutableListOf<HomeworkEntity>()
    var updateCalls = 0
        private set
    private var seq = 1L
    override fun getAll(): List<HomeworkEntity> = items.sortedBy { it.dueDateComputed }.toList()
    override fun getById(id: Long): HomeworkEntity? = items.firstOrNull { it.id == id }
    override fun insert(e: HomeworkEntity): Long {
        val id = seq++
        items.add(e.copy(id = id))
        return id
    }
    override fun update(e: HomeworkEntity) {
        updateCalls++
        val i = items.indexOfFirst { it.id == e.id }
        if (i >= 0) items[i] = e
    }
    override fun deleteById(id: Long): Int {
        val n = items.count { it.id == id }
        items.removeAll { it.id == id }
        return n
    }
}

class OverrideServiceTest {

    @Test
    fun globalWinsOverWeekday() {
        val svc = OverrideService(FakeOverrideDao())
        svc.addOrUpdate("лек ВЫСШ. МАТЕМАТ", "weekday:1", "Матан Пн", null)
        assertEquals("Матан Пн", svc.displayName("лек ВЫСШ. МАТЕМАТ", 1))
        assertEquals("лек ВЫСШ. МАТЕМАТ", svc.displayName("лек ВЫСШ. МАТЕМАТ", 2))
        svc.addOrUpdate("лек ВЫСШ. МАТЕМАТ", "global", "МАТАН!!!", "сдать!")
        assertEquals("МАТАН!!!", svc.displayName("лек ВЫСШ. МАТЕМАТ", 1))
        assertEquals("МАТАН!!!", svc.displayName("лек ВЫСШ. МАТЕМАТ", 2))
        assertEquals("сдать!", svc.note("лек ВЫСШ. МАТЕМАТ", 1))
        assertEquals(2, svc.all().size)
        svc.remove(svc.all().first { it.scope == "global" }.id)
        assertEquals("Матан Пн", svc.displayName("лек ВЫСШ. МАТЕМАТ", 1))
    }
}

class HomeworkServiceTest {

    private val lessons by lazy { GroupParser.parse(GROUP_FIXTURE).lessons }
    private val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)

    private fun svc(dao: FakeHomeworkDao = FakeHomeworkDao()) = HomeworkService(
        dao,
        lessonsFor = { gid, dow, parity ->
            lessons.filter { it.groupId == gid && it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
        },
        ctx = { ctx }
    )

    @Test fun stale_due_text_only_preview_matches_real_save() = checkEditedPreview("2026-09-02")

    @Test fun null_due_text_only_preview_matches_real_save() = checkEditedPreview(null)

    private fun checkEditedPreview(storedDue: String?) {
        val dao = FakeHomeworkDao()
        val service = svc(dao)
        val created = LocalDate.of(2026, 9, 1)
        val today = LocalDate.of(2026, 9, 12)
        val id = service.addHomework("лек ВЫСШ. МАТЕМАТ", "§5", 1, created)
        dao.update(requireNotNull(dao.getById(id)).copy(dueDateComputed = storedDue))
        val stored = requireNotNull(service.getById(id))
        fun open(): HomeworkEditorState {
            val current = requireNotNull(service.getById(id))
            return HomeworkEditorState(id, current.norm, current.norm, current.text, current.n, true,
                homeworkEditorDueFor(current, today) { from, n -> service.computeDueDate(current.norm, from, n) })
        }
        fun label(due: LocalDate?) = if (due == null) "Срок: —" else
            "Срок: ${LessonFormat.dayMonth(due)} (${LessonFormat.weekdayShort(due, XmlCopy)})"
        val copy = XmlCopy
        val opened = open()
        val writes = dao.updateCalls
        val changed = opened.withText("  §6 \n")
        // Discard the edited state and reopen, just like Cancel: no save path executed.
        assertEquals(label(stored.due), open().dueText(copy))
        assertEquals(stored, service.getById(id))
        assertEquals(writes, dao.updateCalls)
        for (noop in listOf(opened, opened.withText("  §5 \n"), opened.inc().dec(), changed.withText("§5"), changed.inc().withText("§5").dec())) {
            assertEquals(label(stored.due), noop.dueText(copy))
            assertEquals(false, noop.hasChanges(stored))
            if (noop.hasChanges(stored)) service.updateHomework(id, noop.text.trim(), noop.n)
        }
        assertEquals(writes, dao.updateCalls)
        assertEquals(stored, service.getById(id))
        assertEquals(LocalDate.of(2026, 9, 7), service.computeDueDate(stored.norm, created, 1))
        assertEquals(LocalDate.of(2026, 9, 14), service.computeDueDate(stored.norm, today, 1))
        for (edited in listOf(changed, changed.inc().dec(), opened.inc(), changed.inc(), changed.inc().withText("§5"))) {
            assertTrue(edited.hasChanges(stored))
            val preview = edited.dueText(copy)
            service.updateHomework(id, edited.text.trim(), edited.n)
            val saved = requireNotNull(service.getById(id))
            assertEquals(label(saved.due), preview)
            assertEquals(created, saved.createdAt)
            assertEquals(stored.norm, saved.norm)
            assertEquals(edited.text.trim(), saved.text)
            assertEquals(edited.n, saved.n)
        }
    }

    @Test fun new_editor_preview_matches_real_add_from_today() {
        val service = svc()
        val today = LocalDate.of(2026, 9, 12)
        val raw = "лек ВЫСШ. МАТЕМАТ"
        val norm = Parity.normalizeSubject(raw)
        val editor = HomeworkEditorState(null, raw, raw, "§6", 1, false,
            homeworkEditorDueFor(null, today) { from, n -> service.computeDueDate(norm, from, n) })
        assertEquals("Срок: 14.09 (Пн)", editor.dueText(XmlCopy))
        val id = service.addHomework(raw, editor.text, editor.n, today)
        assertEquals(editor.dueFor(editor.n, editor.text), service.getById(id)?.due)
        assertEquals(today, service.getById(id)?.createdAt)
    }

    @Test fun unavailable_schedule_text_edit_preview_and_real_save_remain_null() {
        val service = HomeworkService(FakeHomeworkDao(), { _, _, _ -> emptyList() }, { ctx })
        val today = LocalDate.of(2026, 9, 12)
        val id = service.addHomework("лек ВЫСШ. МАТЕМАТ", "§5", 1, today.minusDays(11))
        val stored = requireNotNull(service.getById(id))
        val editor = HomeworkEditorState(id, stored.norm, stored.norm, stored.text, stored.n, true,
            homeworkEditorDueFor(stored, today) { from, n -> service.computeDueDate(stored.norm, from, n) }).withText("§6")
        assertEquals("Срок: —", editor.dueText(XmlCopy))
        service.updateHomework(id, editor.text, editor.n)
        assertEquals(null, service.getById(id)?.due)
    }

    @Test
    fun dueN2() {
        val s = svc()
        val due = s.computeDueDate(Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ"), LocalDate.of(2026, 9, 1), 2)
        // Norm includes type prefix: Mon 09-07 even 09:00 "лек ..." = 1st, Mon 09-14 odd 09:00 = 2nd
        // (Wed 09-09 even 14:55 is "пр ...", different norm)
        assertEquals(LocalDate.of(2026, 9, 14), due)
    }

    @Test
    fun statuses() {
        val s = svc()
        val due = LocalDate.of(2026, 9, 14)
        val norm = Parity.normalizeSubject("лек ВЫСШ. МАТЕМАТ")
        val created = LocalDate.of(2026, 9, 1)
        assertEquals("burning_urgent", s.computeStatus(norm, created, 2, due, false, LocalDate.of(2026, 9, 14)))
        assertEquals("burning", s.computeStatus(norm, created, 2, due, false, LocalDate.of(2026, 9, 13)))
        assertEquals("approaching", s.computeStatus(norm, created, 2, due, false, LocalDate.of(2026, 9, 2)))
        assertEquals("overdue", s.computeStatus(norm, created, 2, due, false, LocalDate.of(2026, 9, 15)))
        assertEquals("done", s.computeStatus(norm, created, 2, due, true, LocalDate.of(2026, 9, 2)))
        assertEquals("far", s.computeStatus(norm, created, 2, LocalDate.of(2026, 12, 1), false, LocalDate.of(2026, 9, 2)))
    }

    @Test
    fun crudAndRecompute() {
        val dao = FakeHomeworkDao()
        val s = svc(dao)
        val id = s.addHomework("лек ВЫСШ. МАТЕМАТ", "с. 10 № 5", 2, LocalDate.of(2026, 9, 1))
        assertEquals(1, dao.items.size)
        assertEquals(LocalDate.of(2026, 9, 14).toString(), dao.items[0].dueDateComputed)
        val list = s.forSubject("лек ВЫСШ. МАТЕМАТ")
        assertEquals(1, list.size)
        assertEquals("с. 10 № 5", list[0].text)
        s.markDone(id, true)
        assertEquals("done", dao.getById(id)!!.status)
        s.updateHomework(id, "с. 10 № 5, правка", 2)
        val after = dao.getById(id)!!
        assertEquals("с. 10 № 5, правка", after.text)
        assertEquals("done", after.status)
        s.markDone(id, false)
        assertTrue(dao.getById(id)!!.status != "done")
        s.delete(id)
        assertTrue(dao.items.isEmpty())
    }

    @Test fun unchanged_schedule_save_and_completion_preserve_due_and_creation() {
        val dao = FakeHomeworkDao()
        val service = svc(dao)
        val created = LocalDate.of(2026, 9, 1)
        val id = service.addHomework("лек ВЫСШ. МАТЕМАТ", "§5", 1, created)
        val original = requireNotNull(service.getById(id))
        for (done in listOf(true, false)) {
            service.markDone(id, done)
            val toggled = requireNotNull(service.getById(id))
            assertEquals(original.due, toggled.due)
            assertEquals(created, toggled.createdAt)
            service.updateHomework(id, original.text, original.n)
            assertEquals(original.due, service.getById(id)?.due)
        }
    }

    @Test fun audit_september_02_vs_14_preview_and_unchanged_save_regression() {
        val dao = FakeHomeworkDao()
        val norm = lessons.first().subjectNormalized
        val daily = HomeworkService(dao, { _, _, _ -> listOf(lessons.first()) }, { ctx })
        val created = LocalDate.of(2026, 9, 1)
        val today = LocalDate.of(2026, 9, 12)
        val id = daily.addHomework(lessons.first().subjectRaw, "§5", 1, created)
        val original = requireNotNull(daily.getById(id))
        assertEquals(LocalDate.of(2026, 9, 2), original.due)
        // The old VM always passed today: reproduce the exact audit discrepancy without Room.
        assertEquals(LocalDate.of(2026, 9, 14), daily.computeDueDate(norm, today, 1))
        val preview = homeworkEditorDueFor(original, today) { from, n -> daily.computeDueDate(norm, from, n) }
        val editor = HomeworkEditorState(id, norm, norm, original.text, 1, true, preview)
        assertEquals(original.due, editor.dueFor(editor.n, editor.text))
        if (editor.hasChanges(original)) daily.updateHomework(id, editor.text.trim(), editor.n)
        assertEquals(original, daily.getById(id))
    }

    @Test fun stale_cached_due_is_preserved_on_unchanged_editor_but_domain_recalculates_on_real_edit() {
        val dao = FakeHomeworkDao()
        val service = svc(dao)
        val id = service.addHomework("лек ВЫСШ. МАТЕМАТ", "§5", 1, LocalDate.of(2026, 9, 1))
        val original = requireNotNull(service.getById(id))
        val row = requireNotNull(dao.getById(id))
        dao.update(row.copy(dueDateComputed = null, status = "pending"))
        val pending = requireNotNull(service.getById(id))
        val editor = HomeworkEditorState(id, pending.norm, pending.norm, pending.text, pending.n, true,
            homeworkEditorDueFor(pending, LocalDate.of(2026, 9, 12)) { from, n -> service.computeDueDate(pending.norm, from, n) })
        if (editor.hasChanges(pending)) service.updateHomework(id, editor.text.trim(), editor.n)
        assertEquals(pending, service.getById(id))
        assertEquals(null, editor.dueFor(editor.n, editor.text))
        // Existing domain behavior, NOT fixed here: a real edit recomputes from current schedule.
        service.updateHomework(id, "§6", pending.n)
        assertEquals(original.due, service.getById(id)?.due)
    }
}

class ChosenHomeworkTest {
    private fun lesson(teacher: String, parity: Int, index: Int) = Lesson(
        groupId = "3313",
        dayOfWeek = 1,
        parity = parity,
        index = index,
        timeStart = "09:00",
        timeEnd = "10:35",
        subjectRaw = "пр ИН. ЯЗ.",
        subjectNormalized = "ин. яз.",
        teacherRaw = teacher,
        classroomRaw = if (index == 1) "101;" else "202;"
    )

    @Test fun chosen_subgroup_skips_the_other_teachers_monday() {
        val ivanov = lesson("Иванов И.И.", 0, 1)
        val petrov = lesson("Петров П.П.", 1, 2)
        val all = listOf(ivanov, petrov)
        val stream = Subgroups.index(all).streams.single()
        val choices = mapOf(stream.id to "петров п п")
        val ctx = SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)
        val due = HomeworkDue.date(
            { _, dow, parity -> HomeworkDue.lessonsOnChosenDay(all, choices, dow, parity) },
            ctx,
            "ин. яз.",
            LocalDate.of(2026, 9, 6),
            1
        )
        assertEquals(LocalDate.of(2026, 9, 14), due)
    }

    @Test fun evening_alarm_keeps_homework_on_the_wall_clock_day() {
        val today = LocalDate.of(2026, 9, 14)
        val clock = notificationClock(today, "20:00", "20:00")
        assertEquals(LocalDate.of(2026, 9, 15), clock.content)
        assertEquals(today, clock.homework)
        val morning = notificationClock(today, "07:30", "20:00")
        assertEquals(today, morning.content)
        assertEquals(today, morning.homework)
        val service = HomeworkService(FakeHomeworkDao(), { _, _, _ -> emptyList() }, {
            SchedCtx("3313", LocalDate.of(2026, 9, 1), 2, false)
        })
        val norm = "лек высш. математ"
        assertEquals("burning_urgent", service.computeStatus(norm, LocalDate.of(2026, 9, 1), 1, today, false, clock.homework))
        assertEquals("overdue", service.computeStatus(norm, LocalDate.of(2026, 9, 1), 1, today, false, clock.content))
    }
}
