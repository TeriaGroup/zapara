package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*

@Composable
internal fun HomeworkRescheduleSheet(batch: HomeworkRescheduleBatch, onEvent: (HomeworkEvent) -> Unit) {
    ZBottomSheet({ onEvent(HomeworkEvent.ClosePostpone) }, "Homework.PostponePreview", scrollable = true,
        canDismiss = { !batch.busy }) {
        Text(stringResource(R.string.ux300_ext_postpone), style = Zapara.typography.section)
        Text(stringResource(R.string.ux300_ext_postpone_hint), style = Zapara.typography.caption)
        batch.rows.forEach { row ->
            ZCard(Modifier.fillMaxWidth()) {
                Text(row.subject, style = Zapara.typography.bodyStrong)
                Text(row.before.text, style = Zapara.typography.body)
                Text("${row.before.due ?: "—"} → ${row.afterDue ?: "—"}", style = Zapara.typography.body)
                Text(stringResource(when {
                    !row.eligible -> R.string.ux300_ext_postpone_skipped
                    row.status == "applied" -> R.string.ux300_ext_postpone_applied
                    row.status == "restored" -> R.string.ux300_ext_postpone_restored
                    row.status == "undoFailed" -> R.string.ux300_ext_postpone_undo_failed
                    row.status == "failed" -> R.string.ux300_ext_postpone_failed
                    else -> R.string.ux300_ext_postpone_ready
                }), style = Zapara.typography.caption)
            }
        }
        ZButton(stringResource(R.string.ux300_ext_postpone_confirm), { onEvent(HomeworkEvent.ConfirmPostpone) },
            enabled = !batch.busy && batch.rows.any { it.eligible && it.status in setOf("pending", "failed") },
            tag = "Homework.PostponeConfirm")
        if (batch.rows.any { it.status in setOf("applied", "undoFailed") }) ZButton(stringResource(R.string.homework_browse_undo),
            { onEvent(HomeworkEvent.UndoPostpone) }, enabled = !batch.busy, ghost = true, tag = "Homework.PostponeUndo")
        ZButton(stringResource(R.string.theme_cancel), { onEvent(HomeworkEvent.ClosePostpone) },
            enabled = !batch.busy, ghost = true)
    }
}
