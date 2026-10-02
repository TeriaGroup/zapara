package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile
import ru.bgtu_voenmeh.zapara.ui.homework.*
import ru.bgtu_voenmeh.zapara.ui.summary.*
import ru.bgtu_voenmeh.zapara.ui.widgets.validWidgetDaySpan
import java.time.LocalDate

class ExtensionPresentationTest {
    @Test fun summary_slots_keep_selected_parity_and_actual_source_rows() {
        val lessons = listOf(Lesson(dayOfWeek=1,parity=1,timeStart="09:00",timeEnd="10:35",typeRaw="лек",subjectRaw="A",teacherRaw="One"),
            Lesson(dayOfWeek=2,parity=2,timeStart="12:00",timeEnd="13:35",typeRaw="лек",subjectRaw="B",teacherRaw="Two"))
        val tiles = SummaryComposer.tiles(0,lessons,{_,_->""}, XmlCopy)
        assertEquals(1,tiles.lessonSlots.getValue(2).values.flatten().size)
        assertEquals("A",tiles.lessonSlots.getValue(2).values.flatten().single().subject)
        assertTrue(summaryMatches("Higher mathematics", "MATH higher"))
        assertFalse(summaryMatches("Higher mathematics", "math physics"))
    }
    @Test fun widget_day_span_ignores_bad_reversed_times_and_orders_clock_values() {
        assertEquals("09:00–15:00",validWidgetDaySpan(listOf(
            Lesson(timeStart="09:00",timeEnd="10:35"), Lesson(timeStart="13:25",timeEnd="15:00"),
            Lesson(timeStart="22:00",timeEnd="01:00"), Lesson(timeStart="bad",timeEnd="23:59"))))
    }
    @Test fun personal_plan_uses_real_deadline_and_file_names_without_paths_or_file_bytes() {
        val item = HomeworkItemUi(1,"Math","Solve 4","", "",false,subjectRaw="raw",due=LocalDate.of(2026,10,4),
            files=listOf(HomeworkStoredFile("private-storage-id","document","task.pdf","application/pdf")))
        val text=homeworkPlanText(listOf(item),"Unknown")
        assertTrue(text.contains("2026-10-04")); assertTrue(text.contains("task.pdf")); assertFalse(text.contains("private-storage-id"))
        val entry=homeworkCalendarEntries(listOf(item),"group").single()
        assertEquals(item.due,entry.day); assertTrue(entry.id.startsWith("homework:"))
        assertEquals(entry.id,homeworkCalendarEntries(listOf(item.copy(subject="Alias")),"group").single().id)
    }
}
