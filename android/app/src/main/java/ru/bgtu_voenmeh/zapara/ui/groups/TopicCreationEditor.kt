@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.communities.*
import ru.bgtu_voenmeh.zapara.ui.components.*
import ru.bgtu_voenmeh.zapara.ui.theme.*

@Composable internal fun TopicCreationEditor(draft:TopicCreationDraft,state:GroupUiState,onChange:(TopicCreationDraft)->Unit,onCreate:()->Unit,onCancel:()->Unit) {
    val text=rememberUiText()
    val custom=draft.canCustomize(state)
    ZCard(Modifier.fillMaxWidth(),tag="Group.TopicCreation") {
        Text(stringResource(R.string.channel_new),style=Zapara.typography.section)
        OutlinedTextField(draft.title,{onChange(draft.copy(title=it.take(40)))},label={Text(stringResource(R.string.channel_name))},modifier=Modifier.fillMaxWidth())
        OutlinedTextField(draft.description,{onChange(draft.copy(description=it.take(240)))},label={Text(stringResource(R.string.channel_description))},modifier=Modifier.fillMaxWidth())
        OutlinedTextField(draft.icon,{onChange(draft.copy(icon=it.take(8)))},label={Text(stringResource(R.string.channel_icon))},modifier=Modifier.fillMaxWidth())
        FlowRow { GroupTemplates.titles.filterKeys { it in (state.space?.capabilities?.templates ?: listOf("chat","polls")) }.forEach { (kind,label) ->
            ZChip(label,selected=draft.template==kind,onClick={onChange(draft.copy(template=kind))})
        } }
        if(draft.template=="subject") FlowRow { state.subjects.forEach { subject -> ZChip(subject,selected=draft.subject==subject,onClick={onChange(draft.copy(subject=subject))}) } }
        FlowRow {
            ZChip(text(R.string.space_day_47),selected=draft.categoryId==null,onClick={onChange(draft.copy(categoryId=null))})
            state.space?.categories.orEmpty().forEach { category -> ZChip(category.title,selected=draft.categoryId==category.categoryId,onClick={onChange(draft.copy(categoryId=category.categoryId))}) }
        }
        OutlinedTextField(draft.position,{onChange(draft.copy(position=it))},label={Text(text(R.string.space_day_48))},modifier=Modifier.fillMaxWidth())
        Row(verticalAlignment=Alignment.CenterVertically) { Checkbox(draft.pinned,{onChange(draft.copy(pinned=it))}); Text(stringResource(R.string.channel_pin)) }
        FlowRow {
            ZChip(stringResource(R.string.channel_writes_all),selected=draft.writePolicy=="all",onClick={onChange(draft.copy(writePolicy="all"))})
            ZChip(stringResource(R.string.channel_writes_managers),selected=draft.writePolicy=="managers",onClick={onChange(draft.copy(writePolicy="managers"))})
        }
        FlowRow {
            listOf("default" to R.string.channel_accent_default,"blue" to R.string.channel_accent_blue,"green" to R.string.channel_accent_green,
                "purple" to R.string.channel_accent_purple,"orange" to R.string.channel_accent_orange,"red" to R.string.channel_accent_red).forEach { (accent,label) ->
                ZChip(text(label),selected=draft.accent==accent,onClick={onChange(draft.copy(accent=accent))})
            }
        }
        if(!custom) Text(text(if(state.space==null) R.string.creation_legacy_access else R.string.creation_access_required),style=Zapara.typography.caption,color=Zapara.colors.warn)
        Text(text(R.string.review_acl_limits),style=Zapara.typography.caption)
        listOf("read" to draft.read,"post" to draft.post).forEach { (power,selection) ->
            Text(stringResource(powerResource(power)),style=Zapara.typography.section)
            val options=if(power=="read") listOf(GroupAccessPresets.Mode.All to R.string.review_read_all,GroupAccessPresets.Mode.Selected to R.string.review_selected_roles)
                else listOf(GroupAccessPresets.Mode.All to R.string.review_post_inherited,GroupAccessPresets.Mode.Selected to R.string.review_selected_roles,GroupAccessPresets.Mode.Headman to R.string.review_headman_only)
            FlowRow { options.forEach { (mode,label) ->
                ZButton(text(label),{ val changed=GroupAccessPresets.Selection(mode,if(mode==GroupAccessPresets.Mode.Selected) selection.roles else emptySet()); onChange(if(power=="read") draft.copy(read=changed) else draft.copy(post=changed)) },enabled=custom || mode==GroupAccessPresets.Mode.All,ghost=selection.mode!=mode)
            } }
            if(selection.mode==GroupAccessPresets.Mode.Selected) state.desk?.roles.orEmpty().forEach { role -> Row(verticalAlignment=Alignment.CenterVertically) {
                Checkbox(role.roleId in selection.roles,{enabled -> val changed=selection.copy(roles=if(enabled) selection.roles+role.roleId else selection.roles-role.roleId); onChange(if(power=="read") draft.copy(read=changed) else draft.copy(post=changed)) },enabled=custom)
                Text(role.name,style=Zapara.typography.body)
            } }
        }
        state.spaceError?.let { Text(it,color=Zapara.colors.bad,style=Zapara.typography.body) }
        Row(horizontalArrangement=Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.channel_create),onCreate,enabled=draft.canSubmit(state),busy=state.channelBusy || state.creationSubmitting)
            ZButton(stringResource(R.string.channel_cancel),onCancel,ghost=true)
        }
    }
}
