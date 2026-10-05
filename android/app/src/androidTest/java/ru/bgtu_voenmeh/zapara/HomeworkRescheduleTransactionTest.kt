package ru.bgtu_voenmeh.zapara

import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.HomeworkService
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.db.HomeworkEntity
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkRescheduleConflict
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkRescheduleRow
import ru.bgtu_voenmeh.zapara.ui.homework.applyVerifiedReschedule
import java.time.LocalDate
import java.util.concurrent.Callable

/** Uses only a new in-memory DB, never the installed application's profile database. */
@RunWith(AndroidJUnit4::class)
class HomeworkRescheduleTransactionTest {
    @Test fun changing_subgroup_projection_during_apply_rolls_back_real_room_write() = verify(false)
    @Test fun changing_subgroup_projection_during_undo_rolls_back_real_room_write() = verify(true)

    private fun verify(undo: Boolean) {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val db = Room.inMemoryDatabaseBuilder(context, ZaparaDatabase::class.java).build()
        try {
            val created = LocalDate.of(2026, 10, 1)
            val beforeDue = LocalDate.of(2026, 10, 2)
            val afterDue = LocalDate.of(2026, 10, 9)
            val id = db.homeworkDao().insert(HomeworkEntity(subjectRawNormalized = "math", text = "Task",
                createdAt = created.toString(), targetNthOccurrence = if (undo) 2 else 1,
                dueDateComputed = (if (undo) afterDue else beforeDue).toString(), status = "soon"))
            val original = db.homeworkDao().getById(id)
            var selectedDay = 5
            val service = HomeworkService(db.homeworkDao(), { group, day, _ ->
                if (day == selectedDay) listOf(Lesson(groupId = group, dayOfWeek = day,
                    subjectRaw = "Math", subjectNormalized = "math")) else emptyList()
            }, { SchedCtx("group", LocalDate.of(2026, 9, 1), 2, false) })
            val preview = HomeworkRescheduleRow(Homework(id, "math", "Task", created, 1, beforeDue,
                "soon", false), "Math", afterDue)
            assertThrows(HomeworkRescheduleConflict::class.java) {
                db.runInTransaction(Callable {
                    applyVerifiedReschedule(preview, undo,
                        read = { service.getById(id) },
                        compute = { n -> service.computeDueDate("math", created, n) },
                        write = { text, n ->
                            // SharedPreferences is outside Room's lock: updateCore sees the new selection.
                            selectedDay = 6
                            service.updateHomework(id, text, n)
                        })
                })
            }
            assertEquals(original, db.homeworkDao().getById(id))
        } finally { db.close() }
    }
}
