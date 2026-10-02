package ru.bgtu_voenmeh.zapara.ui.week

import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

@Composable internal fun AcademicAgendaTools(state: WeekUiState, open: (LocalDate, Lesson) -> Unit) {
    var panel by remember(state.profileName, state.groupId) { mutableStateOf<String?>(null) }
    ZActionButton(stringResource(R.string.ux300_agenda_assessments), { panel = "assessments" }, tag = "Week.Assessments")
    if (panel != null) ZBottomSheet({ panel = null }, "Week.AcademicAgenda", scrollable = true) {
        Text(stringResource(if (panel == "assessments") R.string.ux300_agenda_assessments else R.string.ux300_agenda_conflicts), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_agenda_scope), style = Zapara.typography.caption)
        fun jump(date: LocalDate, lesson: Lesson) { panel = null; open(date, lesson) }
        if (panel == "assessments") {
            Text("${agendaDateLabel(state.selectedDate)} — ${agendaDateLabel(state.selectedDate.plusDays(27))}", style = Zapara.typography.caption)
            if (state.assessmentUnknownDays > 0) Text(stringResource(R.string.ux300_agenda_unknown_days, state.assessmentUnknownDays), style = Zapara.typography.caption)
            if (state.assessments.isEmpty()) Text(stringResource(R.string.ux300_agenda_no_assessments), style = Zapara.typography.body)
            state.assessments.forEach { row -> ZCard(Modifier.fillMaxWidth()) {
                Text("${agendaDateLabel(row.date)} · ${row.lesson.timeStart}–${row.lesson.timeEnd}", style = Zapara.typography.caption)
                Text(row.lesson.subjectRaw, style = Zapara.typography.bodyStrong)
                Text("${row.lesson.teacherRaw} · ${row.lesson.classroomRaw}", style = Zapara.typography.body)
                ZActionButton(stringResource(R.string.ux300_agenda_open_lesson), { jump(row.date, row.lesson) })
            } }
        }
    }
}
