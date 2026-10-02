package ru.bgtu_voenmeh.zapara.ui
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.week.*
import ru.bgtu_voenmeh.zapara.ui.homework.*
import java.time.LocalDate

class WeekPlanningTest {
    @Test fun comparison_preserves_multiplicity_and_ignores_display_aliases() {
        val date=LocalDate.of(2026,10,5)
        val row=WeekRowUi("09:00–10:35","Alias","Room",start="09:00",end="10:35",subjectRaw="Raw",teacherRaw="Teacher",classroomRaw="320")
        val before=listOf(WeekDayUi(1,"",date,listOf(row,row),false))
        val after=listOf(WeekDayUi(1,"",date.plusWeeks(1),listOf(row.copy(name="Other alias")),false))
        val diff=compareWeeks(before,after).single()
        assertEquals(1,diff.removed.size); assertTrue(diff.added.isEmpty())
        assertEquals(date.plusWeeks(1),diff.afterDate)
        val roomChange=compareWeeks(before.take(1).map { it.copy(rows=listOf(row)) },after.map { it.copy(rows=listOf(row.copy(classroomRaw="321"))) }).single()
        assertEquals(1,roomChange.added.size); assertEquals(1,roomChange.removed.size)
    }
    @Test fun subject_planner_counts_personal_deadlines_and_groups_raw_identity() {
        val today=LocalDate.of(2026,10,2)
        val first=HomeworkItemUi(1,"Alias","Task","","",false,subjectRaw="Math",due=today.minusDays(1))
        val plans=homeworkSubjectPlans(listOf(first,first.copy(id=2,subject="Other",due=null),first.copy(id=3,done=true)),today)
        val plan=plans.single()
        assertEquals(2,plan.active); assertEquals(1,plan.done); assertEquals(1,plan.overdue); assertEquals(1,plan.undated)
        assertEquals(today.minusDays(1),plan.nextDate)
    }
}
