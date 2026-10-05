package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.LocalDate

internal fun homeworkSubjectKey(item: HomeworkItemUi): String = Parity.normalizeSubject(item.subjectRaw.ifBlank { item.subject })
data class HomeworkSubjectPlan(val key: String, val label: String, val active: Int, val done: Int,
    val overdue: Int, val undated: Int, val nextDate: LocalDate?)
internal fun homeworkSubjectPlans(rows: List<HomeworkItemUi>, today: LocalDate): List<HomeworkSubjectPlan> =
    rows.groupBy(::homeworkSubjectKey).map { (key, tasks) ->
        val active = tasks.filterNot { it.done }
        HomeworkSubjectPlan(key, tasks.first().subject, active.size, tasks.count { it.done },
            active.count { it.due?.isBefore(today) == true }, active.count { it.due == null },
            active.mapNotNull { it.due }.minOrNull())
    }.sortedWith(compareByDescending<HomeworkSubjectPlan> { it.overdue }.thenBy { it.nextDate ?: LocalDate.MAX }.thenBy { it.label })

@Composable
internal fun HomeworkSubjectOverview(items: List<HomeworkItemUi>, scope: String, onSubject: (String) -> Unit) {
    var open by remember(scope) { mutableStateOf(false) }
    if (items.isEmpty()) return
    ZActionButton(stringResource(R.string.ux300_ext_subject_plan), { open = true }, tag = "Homework.SubjectPlan")
    if (open) ZBottomSheet({ open = false }, "Homework.SubjectOverview", scrollable = true) {
        Text(stringResource(R.string.ux300_ext_subject_plan), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_ext_subject_plan_hint), style = Zapara.typography.caption)
        homeworkSubjectPlans(items, LocalDate.now()).forEach { plan ->
            ZCard(Modifier.fillMaxWidth()) {
                Text(plan.label, style = Zapara.typography.bodyStrong)
                Text(stringResource(R.string.ux300_ext_subject_plan_counts, plan.active, plan.done, plan.overdue, plan.undated),
                    style = Zapara.typography.body)
                plan.nextDate?.let { Text(stringResource(R.string.ux300_ext_subject_next, it.toString()), style = Zapara.typography.caption) }
                ZActionButton(stringResource(R.string.ux300_ext_open_subject_tasks), { onSubject(plan.key); open = false })
            }
        }
    }
}
