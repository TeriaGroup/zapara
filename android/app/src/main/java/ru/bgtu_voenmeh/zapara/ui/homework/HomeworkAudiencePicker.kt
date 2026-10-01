@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package ru.bgtu_voenmeh.zapara.ui.homework

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText

data class AudienceChoice(val id: String, val label: String)

@Composable
fun HomeworkAudiencePicker(
    key: String,
    audience: HomeworkAudience,
    roles: List<AudienceChoice>,
    people: List<AudienceChoice>,
    enabled: Boolean,
    onChange: (HomeworkAudience) -> Unit
) {
    val uiText = rememberUiText()
    var query by rememberSaveable(key) { mutableStateOf("") }
    var visiblePeople by rememberSaveable(key, query) { mutableIntStateOf(30) }
    Text(uiText(R.string.homework_audience_title), style = Zapara.typography.bodyStrong)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        ZChip(uiText(R.string.homework_audience_all), selected = !audience.selected, onClick = { if (enabled) onChange(HomeworkAudience()) })
        ZChip(uiText(R.string.homework_audience_selected), selected = audience.selected, onClick = {
            if (enabled && !audience.selected) onChange(HomeworkAudience("selected"))
        })
    }
    if (!audience.selected) {
        Text(uiText(R.string.homework_audience_all_hint), style = Zapara.typography.caption)
        return
    }
    Text(uiText(R.string.homework_audience_union_hint), style = Zapara.typography.caption)
    val chosenRoles = roles.filter { it.id in audience.roleIds }
    val chosenPeople = people.filter { it.id in audience.userIds }
    Text(uiText(R.string.ux30_study_audience_summary, audience.roleIds.size, audience.userIds.size),
        style = Zapara.typography.caption, color = Zapara.colors.text2)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        chosenRoles.forEach { role -> ZChip(uiText(R.string.homework_audience_selected_chip, role.label), selected = true, onClick = { if (enabled) onChange(audience.copy(roleIds = audience.roleIds - role.id)) }) }
        chosenPeople.forEach { person -> ZChip(uiText(R.string.homework_audience_selected_chip, person.label), selected = true, onClick = { if (enabled) onChange(audience.copy(userIds = audience.userIds - person.id)) }) }
    }
    if (chosenRoles.isNotEmpty() || chosenPeople.isNotEmpty()) ZButton(uiText(R.string.homework_audience_clear), { onChange(HomeworkAudience("selected")) }, enabled = enabled, ghost = true)
    Text(uiText(R.string.homework_audience_roles), style = Zapara.typography.bodyStrong)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        roles.forEach { role ->
            ZChip(role.label, selected = role.id in audience.roleIds, onClick = {
                if (enabled) onChange(audience.copy(roleIds = if (role.id in audience.roleIds) audience.roleIds - role.id else audience.roleIds + role.id))
            })
        }
    }
    Text(uiText(R.string.homework_audience_people), style = Zapara.typography.bodyStrong)
    ZTextField(query, { query = it }, label = { Text(uiText(R.string.homework_audience_search)) }, enabled = enabled,
        modifier = Modifier.fillMaxWidth(), trailingIcon = if (query.isNotEmpty()) {{
            ZIconButton(R.drawable.ic_x, uiText(R.string.ux30_study_clear_people_search),
                { query = "" }, "Audience.ClearSearch", enabled = enabled)
        }} else null)
    val matches = people.filter { query.isBlank() || it.label.contains(query, ignoreCase = true) }
    Text(uiText(R.string.homework_audience_shown, minOf(matches.size, visiblePeople), matches.size), style = Zapara.typography.caption)
    if (matches.isEmpty()) Text(uiText(R.string.ux30_study_people_no_results),
        style = Zapara.typography.caption, color = Zapara.colors.text2)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        matches.take(visiblePeople).forEach { person ->
            ZChip(person.label, selected = person.id in audience.userIds, onClick = {
                if (enabled) onChange(audience.copy(userIds = if (person.id in audience.userIds) audience.userIds - person.id else audience.userIds + person.id))
            })
        }
    }
    if (matches.size > visiblePeople) ZButton(uiText(R.string.ux30_study_more_people),
        { visiblePeople += 30 }, ghost = true, enabled = enabled, tag = "Audience.MorePeople")
    if (!audience.valid()) Text(uiText(R.string.homework_audience_empty), style = Zapara.typography.caption, color = Zapara.colors.warn)
}
