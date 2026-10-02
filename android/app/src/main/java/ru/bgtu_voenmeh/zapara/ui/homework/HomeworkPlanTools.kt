package ru.bgtu_voenmeh.zapara.ui.homework

import android.content.Intent
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.calendar.*
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*
import java.time.Instant

internal fun homeworkPlanText(items: List<HomeworkItemUi>, unknownDate: String): String = items.joinToString("\n\n") { item ->
    listOf("${if (item.done) "[x]" else "[ ]"} ${item.subject} · ${item.due ?: unknownDate}", item.text,
        item.files.joinToString(", ") { it.name }).filter(String::isNotBlank).joinToString("\n")
}

internal fun homeworkCalendarEntries(items: List<HomeworkItemUi>, groupId: String) = items.map { item ->
    AllDayCalendarEntry("homework:" + CalendarLessonExport.canonicalId(groupId, item.subjectRaw, item.text, ""),
        item.due, item.subject, item.text)
}

@Composable
internal fun HomeworkPlanTools(items: List<HomeworkItemUi>, groupId: String, profileName: String) {
    val context = LocalContext.current
    val coroutine = rememberCoroutineScope()
    var open by remember(groupId, profileName) { mutableStateOf(false) }
    var busy by remember(groupId, profileName) { mutableStateOf(false) }
    var status by remember(groupId, profileName) { mutableStateOf<String?>(null) }
    val current by rememberUpdatedState(Triple(groupId, profileName, items))
    if (items.isEmpty()) return
    ZButton(stringResource(R.string.ux300_ext_plan_export), { open = true }, ghost = true, tag = "Homework.PlanExport")
    if (open) ZBottomSheet({ if (!busy) open = false }, "Homework.PlanPreview", scrollable = true) {
        Text(stringResource(R.string.ux300_ext_plan_export), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_ext_personal_only, items.size), style = Zapara.typography.caption)
        val text = homeworkPlanText(items, stringResource(R.string.ux300_ext_due_unknown))
        Text(text, style = Zapara.typography.body)
        status?.let { Text(it, style = Zapara.typography.caption) }
        ZButton(stringResource(R.string.ux300_ext_share_plan), {
            try { context.startActivity(Intent.createChooser(AndroidCalendarShare.textIntent(text),
                context.getString(R.string.ux300_ext_share_plan))) }
            catch (_: Exception) { status = context.getString(R.string.ux300_ext_export_failed) }
        }, enabled = !busy, tag = "Homework.SharePlan")
        ZButton(stringResource(R.string.ux300_ext_deadline_calendar), {
            if (!busy) coroutine.launch {
                busy = true
                val snapshot = Triple(groupId, profileName, items)
                try {
                    val result = CalendarExport.createAllDay(homeworkCalendarEntries(items, groupId),
                        context.getString(R.string.ux300_ext_deadline_calendar), Instant.now())
                    status = context.getString(R.string.ux300_ext_export_count, result.eventCount, result.skippedCount)
                    if (result.eventCount > 0) {
                        val uri = withContext(Dispatchers.IO) { AndroidCalendarShare.writeIcs(context, result) }
                        if (current == snapshot) context.startActivity(Intent.createChooser(AndroidCalendarShare.icsIntent(context, uri),
                            context.getString(R.string.ux300_ext_deadline_calendar)))
                    }
                } catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
                catch (_: Exception) { status = context.getString(R.string.ux300_ext_export_failed) }
                finally { busy = false }
            }
        }, enabled = !busy, tag = "Homework.DeadlineCalendar")
    }
}
