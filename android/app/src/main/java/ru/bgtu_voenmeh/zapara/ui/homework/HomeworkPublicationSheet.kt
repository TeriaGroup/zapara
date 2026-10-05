package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.material3.AlertDialog
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.*

@Composable
internal fun HomeworkPublicationSheet(batch: PersonalPublicationBatch, onEvent: (HomeworkEvent) -> Unit) {
    var discard by remember(batch.rows.firstOrNull()?.operationId) { mutableStateOf(false) }
    ZBottomSheet({ onEvent(HomeworkEvent.ClosePublication) }, "Homework.PublicationPreview", scrollable = true,
        canDismiss = { !batch.busy }, footer = {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(if (batch.locked) R.string.ux300_ext_publication_retry else R.string.ux300_ext_publication_confirm),
                    { onEvent(HomeworkEvent.ConfirmPublication) }, modifier = Modifier.fillMaxWidth(),
                    enabled = !batch.busy && batch.rows.any { !it.sent } &&
                        publicationAudienceAvailable(batch.audience, batch.context),
                    busy = batch.busy, tag = "Homework.PublicationConfirm", leadingIcon = R.drawable.ic_send)
                ZButton(stringResource(R.string.ux300_ext_close), { onEvent(HomeworkEvent.ClosePublication) },
                    modifier = Modifier.fillMaxWidth(), enabled = !batch.busy, ghost = true, leadingIcon = R.drawable.ic_x)
                if (batch.locked && batch.rows.any { !it.sent }) ZButton(stringResource(R.string.ux300_ext_publication_abandon),
                    { discard = true }, modifier = Modifier.fillMaxWidth(), enabled = !batch.busy, ghost = true,
                    leadingIcon = R.drawable.ic_trash)
            }
        }) {
        Text(stringResource(R.string.ux300_ext_publish_selected), style = Zapara.typography.section)
        Text(batch.context.groupName, style = Zapara.typography.bodyStrong)
        Text(stringResource(R.string.ux300_ext_publication_hint), style = Zapara.typography.caption)
        HomeworkAudiencePicker("batch:${batch.rows.firstOrNull()?.operationId}", batch.audience,
            batch.context.desk.roles.map { AudienceChoice(it.roleId, it.name) },
            batch.context.people.filterNot { it.self }.map { AudienceChoice(it.userId, it.displayName ?: it.username) },
            !batch.busy && !batch.locked, { onEvent(HomeworkEvent.PublicationAudience(it)) })
        batch.rows.forEach { row ->
            ZCard(Modifier.fillMaxWidth()) {
                Text(row.title, style = Zapara.typography.bodyStrong)
                Text(row.before.text, style = Zapara.typography.body)
                Text(row.before.due?.toString() ?: stringResource(R.string.ux300_ext_due_unknown), style = Zapara.typography.caption)
                Text(stringResource(when {
                    row.sent -> R.string.ux300_ext_publication_sent
                    row.failed && row.attempted -> R.string.ux300_ext_publication_uncertain
                    row.failed -> R.string.ux300_ext_publication_changed
                    else -> R.string.ux300_ext_publication_ready
                }), style = Zapara.typography.caption)
            }
        }
        batch.error?.let { Text(it, style = Zapara.typography.body) }
        if (batch.locked) Text(stringResource(R.string.ux300_ext_publication_retry_hint), style = Zapara.typography.caption)
    }
    if (discard) AlertDialog(onDismissRequest = { discard = false },
        title = { Text(stringResource(R.string.ux300_ext_publication_abandon)) },
        text = { Text(stringResource(R.string.ux300_ext_publication_abandon_hint)) },
        confirmButton = { ZButton(stringResource(R.string.ux300_ext_publication_abandon), {
            discard = false; onEvent(HomeworkEvent.DiscardPublication)
        }, enabled = !batch.busy) },
        dismissButton = { ZButton(stringResource(R.string.theme_cancel), { discard = false }, ghost = true) },
        containerColor = Zapara.colors.card)
}
