@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.Saver
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.compose.ui.platform.LocalContext
import android.app.DatePickerDialog
import ru.bgtu_voenmeh.zapara.data.communities.*
import ru.bgtu_voenmeh.zapara.ui.theme.*
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.components.RevisionGuard
import ru.bgtu_voenmeh.zapara.ui.homework.AudienceChoice
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkAudiencePicker
import java.time.*
import java.time.format.DateTimeFormatter
import java.util.UUID
import kotlinx.coroutines.launch

@Suppress("UNCHECKED_CAST")
private val questionSaver = Saver<List<GroupFormQuestion>, ArrayList<ArrayList<Any>>>(
    save = { rows -> ArrayList(rows.map { arrayListOf<Any>(it.questionId, it.title, it.kind, it.required, ArrayList(it.options)) }) },
    restore = { rows -> rows.map { GroupFormQuestion(it[0] as String, it[1] as String, it[2] as String, it[3] as Boolean, (it[4] as ArrayList<String>).toList()) } }
)
@Suppress("UNCHECKED_CAST")
private val answerSaver = Saver<List<GroupFormAnswer>, ArrayList<ArrayList<Any>>>(
    save = { rows -> ArrayList(rows.map { arrayListOf<Any>(it.questionId, it.text ?: "", ArrayList(it.choices)) }) },
    restore = { rows -> rows.map { GroupFormAnswer(it[0] as String, it[1] as String, (it[2] as ArrayList<String>).toList()) } }
)

private fun dispatch(onEvent: (GroupEvent) -> Unit, action: GroupSpaceAction) = onEvent(GroupEvent.SpaceAction(action))
@Composable private fun Field(label: String, value: String, change: (String) -> Unit, enabled: Boolean = true) {
    val uiText = rememberUiText()
    ZTextField(value, change, label = { Text(label) }, enabled = enabled, modifier = Modifier.fillMaxWidth())
}
private fun displayDeadlineValue(value: Instant?): String = value?.atZone(ZoneId.systemDefault())?.toLocalDate()?.toString() ?: ""
private fun parseDeadline(value: String): Instant? = if (value.isBlank()) null else LocalDate.parse(value).atStartOfDay(ZoneId.systemDefault()).plusDays(1).minusNanos(1).toInstant()
@Composable private fun displayDeadline(value: Instant?): String { val uiText = rememberUiText(); return value?.atZone(ZoneId.systemDefault())?.toLocalDate()?.toString() ?: uiText(R.string.space_day_61) }

@Composable fun GroupSpaceManagement(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    val panel = state.spacePanel ?: return
    var managementQuery by rememberSaveable(state.communityId, panel) { mutableStateOf("") }
    val close = { dispatch(onEvent, GroupSpaceAction.Panel(null)) }
    var selectedRoleId by rememberSaveable(state.communityId) { mutableStateOf<String?>(null) }
    var roleDirty by remember(state.communityId, panel) { mutableStateOf(false) }
    var discardTarget by remember(state.communityId, panel) { mutableStateOf<String?>(null) }
    val requestClose = {
        if (!state.channelBusy) {
            if (panel == "roles" && roleDirty) discardTarget = "__close__" else close()
        }
    }
    BackHandler { requestClose() }
    ZBottomSheet(onDismiss = requestClose, tag = "Group.Space.$panel") {
        Text(when(panel) { "roles" -> uiText(R.string.space_day_62); "categories" -> uiText(R.string.space_day_63); "archive" -> uiText(R.string.space_day_64); "access" -> uiText(R.string.space_day_65); "preview" -> uiText(R.string.space_day_66); else -> uiText(R.string.space_day_67) }, style = Zapara.typography.section)
        state.spaceError?.let { Text(it, color = Zapara.colors.bad, style = Zapara.typography.body) }
        if (state.channelBusy) LinearProgressIndicator(Modifier.fillMaxWidth())
        if (panel in setOf("archive", "categories", "preview")) Field(stringResource(when (panel) {
            "archive" -> R.string.ux100_chat_archive_search; "categories" -> R.string.ux100_chat_category_search
            else -> R.string.ux100_chat_preview_search
        }), managementQuery, { managementQuery = it })
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 600.dp), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            when(panel) {
                "categories" -> {
                    item { CategoryEditor(null, state, onEvent) }
                    items(state.space?.categories.orEmpty().filter { managementQuery.isBlank() || it.title.contains(managementQuery.trim(), true) }.sortedBy { it.position }, key = { it.categoryId }) { CategoryEditor(it, state, onEvent) }
                }
                "archive" -> items(state.archived.filter { managementQuery.isBlank() || (it.title + " " + it.description).contains(managementQuery.trim(), true) }, key = { it.topicId ?: "general" }) { topic ->
                    ZCard(Modifier.fillMaxWidth(),onClick={ topic.topicId?.let { onEvent(GroupEvent.OpenArchived(it)) } }) { Text(topic.title, style = Zapara.typography.bodyStrong); Text(topic.description, style = Zapara.typography.caption)
                        ZButton(uiText(R.string.space_day_68), { dispatch(onEvent, GroupSpaceAction.Archive(topic, false)) }, enabled = !state.channelBusy && GroupActions.canManageTopic(state,topic), ghost = true) }
                }
                "roles" -> {
                    item {
                        val roles = state.desk?.roles.orEmpty().sortedByDescending { it.position }
                        var name by rememberSaveable(state.communityId) { mutableStateOf("") }
                        val selected = roles.firstOrNull { it.roleId == selectedRoleId }
                        val previousIds = remember(state.communityId) { mutableStateOf(roles.map { it.roleId }.toSet()) }
                        LaunchedEffect(roles.map { it.roleId }) {
                            if (selectedRoleId != null && selected == null) selectedRoleId = null
                            val added = roles.firstOrNull { it.roleId !in previousIds.value }
                            if (added != null && added.name.equals(name.trim(), ignoreCase = true)) { selectedRoleId = added.roleId; name = "" }
                            previousIds.value = roles.map { it.roleId }.toSet()
                        }
                        Text(uiText(R.string.group_roles_scope), style = Zapara.typography.caption)
                        Text(uiText(R.string.group_roles_count, roles.size, state.space?.capabilities?.maxRoles ?: 12), style = Zapara.typography.caption)
                        if (state.desk?.headman == true || "roles" in state.desk?.mine.orEmpty()) {
                            Field(uiText(R.string.space_day_70), name, { name = it.take(32) })
                            val mayCreate = state.desk?.headman == true || state.desk?.let { RoleManagementPolicy.actorPosition(it, state.people) > 0 } == true
                            if (!mayCreate) Text(uiText(R.string.group_roles_create_level), style = Zapara.typography.caption)
                            ZButton(uiText(R.string.space_day_71), { dispatch(onEvent, GroupSpaceAction.CreateRole(name.trim())) }, enabled = mayCreate && !state.channelBusy && name.trim().length >= 2 && roles.size < (state.space?.capabilities?.maxRoles ?: 12))
                        }
                        roles.forEach { role ->
                            val grants = state.desk?.grants.orEmpty().count { it.roleId == role.roleId }
                            val powers = state.desk?.powers.orEmpty().filter { it.roleId == role.roleId }.map { powerTitle(it.power) }
                            ZCard(Modifier.fillMaxWidth()) {
                                ZButton(uiText(R.string.group_roles_card, role.icon, role.name, role.position, grants), {
                                    val target = if (selectedRoleId == role.roleId) "" else role.roleId
                                    if (roleDirty) discardTarget = target else selectedRoleId = target.ifEmpty { null }
                                }, modifier = Modifier.fillMaxWidth(), ghost = selectedRoleId != role.roleId,
                                    enabled = !state.channelBusy)
                                Text(powers.take(3).joinToString(" · ").ifBlank { uiText(R.string.group_roles_no_powers) } + if (powers.size > 3) uiText(R.string.group_roles_more_powers, powers.size - 3) else "", style = Zapara.typography.caption)
                            }
                        }
                        if (selected != null) RoleEditor(selected, state, onEvent, { roleDirty = it })
                    }
                }
                "access" -> state.access?.let { access -> item { AccessEditor(access, state, onEvent) } }
                "preview" -> {
                    item { Text(uiText(R.string.space_day_72), style = Zapara.typography.caption)
                        if (state.preview != null) ZButton(uiText(R.string.space_day_73), { dispatch(onEvent, GroupSpaceAction.EndPreview) }, ghost = true) }
                    items(browsePeople(state.people, managementQuery), key = { it.id }) { person -> ZButton("${person.name} · ${officialRoleTitle(person.role)}", { dispatch(onEvent, GroupSpaceAction.Preview(person.id, null)) }, enabled = !state.channelBusy, ghost = true) }
                    items(state.desk?.roles.orEmpty(), key = { it.roleId }) { role -> ZButton(role.name, { dispatch(onEvent, GroupSpaceAction.Preview(null, role.roleId)) }, enabled = !state.channelBusy, ghost = true) }
                }
                "audit" -> items(state.audit, key = { it.eventId }) { event -> ZCard(Modifier.fillMaxWidth()) { Text(event.action, style = Zapara.typography.bodyStrong); Text(uiText(R.string.space_day_75, (event.createdAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm"))).toString(), (state.people.firstOrNull { it.id == event.actorId }?.name ?: uiText(R.string.space_day_74)).toString()), style = Zapara.typography.caption) } }
            }
        }
    }
    if (discardTarget != null) AlertDialog(onDismissRequest = { discardTarget = null },
        title = { Text(stringResource(R.string.next_role_discard_title)) },
        text = { Text(stringResource(R.string.next_role_discard_body)) },
        confirmButton = { ZButton(stringResource(R.string.next_role_discard), {
            val target = discardTarget
            discardTarget = null
            roleDirty = false
            if (target == "__close__") close() else selectedRoleId = target?.ifEmpty { null }
        }) },
        dismissButton = { ZButton(stringResource(R.string.channel_cancel), { discardTarget = null }, ghost = true) })
}

@Composable private fun CategoryEditor(category: GroupCategory?, state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    var initialRevision by remember(category?.categoryId) { mutableStateOf(category?.revision ?: 0) }
    var title by rememberSaveable(category?.categoryId) { mutableStateOf(category?.title ?: "") }
    var position by rememberSaveable(category?.categoryId) { mutableStateOf((category?.position ?: state.space?.categories.orEmpty().size).toString()) }
    var confirmingDelete by remember(category?.categoryId) { mutableStateOf(false) }
    LaunchedEffect(category?.revision) { if (category != null && category.title == title && category.position.toString() == position) initialRevision = category.revision }
    ZCard(Modifier.fillMaxWidth()) {
        if (category != null && category.revision != initialRevision) ZButton(uiText(R.string.space_day_reload_changes), { title = category.title; position = category.position.toString(); initialRevision = category.revision }, ghost = true)
        Field(if (category == null) uiText(R.string.space_day_76) else uiText(R.string.space_day_77), title, { title = it.take(80) })
        Field(uiText(R.string.space_day_78), position, { position = it })
        if (category != null && (title != category.title || position != category.position.toString()))
            ZButton(stringResource(R.string.ux100_chat_category_reset), {
                title = category.title; position = category.position.toString(); initialRevision = category.revision
            }, enabled = !state.channelBusy, ghost = true)
        ZButton(uiText(R.string.space_day_79), { dispatch(onEvent, GroupSpaceAction.Category(category?.categoryId, title.trim(), position.toInt(), initialRevision)) }, enabled = !state.channelBusy && title.trim().length >= 2 && position.toIntOrNull() != null, busy = state.channelBusy)
        if (category != null) ZButton(uiText(R.string.space_day_80), { confirmingDelete = true },
            enabled = !state.channelBusy && (state.desk?.headman == true || "channels" in state.desk?.mine.orEmpty()), ghost = true)
    }
    if (category != null && confirmingDelete) {
        val affected = (state.space?.topics.orEmpty() + state.archived).distinctBy { it.topicId }
            .count { it.categoryId == category.categoryId }
        AlertDialog(onDismissRequest = { confirmingDelete = false },
            title = { Text(stringResource(R.string.next_category_delete_title, category.title)) },
            text = { Text(stringResource(R.string.next_category_delete_body, affected)) },
            confirmButton = { ZButton(stringResource(R.string.next_category_delete), {
                if (state.space?.categories?.any { it.categoryId == category.categoryId } == true &&
                    (state.desk?.headman == true || "channels" in state.desk?.mine.orEmpty()))
                    dispatch(onEvent, GroupSpaceAction.DeleteCategory(category.categoryId, state.communityId))
                confirmingDelete = false
            }, enabled = !state.channelBusy) },
            dismissButton = { ZButton(stringResource(R.string.channel_cancel), { confirmingDelete = false }, ghost = true) })
    }
}

@Composable private fun RoleEditor(role: GroupRole, state: GroupUiState, onEvent: (GroupEvent) -> Unit,
    onDirtyChange: (Boolean) -> Unit) {
    val uiText = rememberUiText()
    val desk = state.desk ?: return
    val settingReason = RoleManagementPolicy.roleReason(desk, state.people, role, "roles")
    val grantReason = RoleManagementPolicy.roleReason(desk, state.people, role, "grants")
    val canSettings = settingReason == null
    val canGrant = grantReason == null
    var initialRevision by remember(role.roleId) { mutableStateOf(role.revision) }
    var name by rememberSaveable(role.roleId) { mutableStateOf(role.name) }
    var icon by rememberSaveable(role.roleId) { mutableStateOf(role.icon) }
    var position by rememberSaveable(role.roleId) { mutableStateOf(role.position.toString()) }
    var section by rememberSaveable(role.roleId) { mutableStateOf("settings") }
    var query by rememberSaveable(role.roleId) { mutableStateOf("") }
    var assignedOnly by rememberSaveable(role.roleId) { mutableStateOf(true) }
    var peopleLimit by rememberSaveable(state.communityId, role.roleId) { mutableIntStateOf(40) }
    var delete by remember { mutableStateOf(false) }
    LaunchedEffect(role.revision) { if (role.name == name && role.icon == icon && role.position.toString() == position) initialRevision = role.revision }
    LaunchedEffect(role.roleId, name, icon, position, role.name, role.icon, role.position) {
        onDirtyChange(name != role.name || icon != role.icon || position != role.position.toString())
    }
    ZCard(Modifier.fillMaxWidth()) {
        Text("${role.icon} ${role.name}", style = Zapara.typography.section)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            listOf("settings" to R.string.group_roles_settings, "powers" to R.string.group_roles_powers, "people" to R.string.group_roles_people).forEach { (key, label) ->
                ZChip(uiText(label), selected = section == key, onClick = { section = key })
            }
        }
        when (section) {
            "settings" -> {
                settingReason?.let { Text(uiText(it), style = Zapara.typography.caption, color = Zapara.colors.warn) }
                if (role.revision != initialRevision) {
                    Text(uiText(R.string.group_roles_conflict), style = Zapara.typography.caption, color = Zapara.colors.warn)
                    ZButton(uiText(R.string.space_day_reload_changes), { name = role.name; icon = role.icon; position = role.position.toString(); initialRevision = role.revision }, ghost = true)
                }
                Field(uiText(R.string.space_day_81), name, { value ->
                    name = value.take(32)
                    onDirtyChange(name != role.name || icon != role.icon || position != role.position.toString())
                }, canSettings)
                Field(uiText(R.string.space_day_82), icon, { value ->
                    icon = value.take(16)
                    onDirtyChange(name != role.name || icon != role.icon || position != role.position.toString())
                }, canSettings)
                Field(uiText(R.string.space_day_83), position, { value ->
                    position = value
                    onDirtyChange(name != role.name || icon != role.icon || position != role.position.toString())
                }, canSettings)
                if (name != role.name || icon != role.icon || position != role.position.toString())
                    ZButton(stringResource(R.string.ux100_chat_role_reset), {
                        name = role.name; icon = role.icon; position = role.position.toString(); initialRevision = role.revision
                        onDirtyChange(false)
                    }, enabled = !state.channelBusy, ghost = true)
                Text(uiText(R.string.group_roles_level_hint), style = Zapara.typography.caption)
                val maxPosition = RoleManagementPolicy.maximumAssignablePosition(desk, state.people)
                val parsedPosition = position.toIntOrNull()
                if (position.isBlank()) Text(stringResource(R.string.next_role_position_required),
                    style = Zapara.typography.caption, color = Zapara.colors.warn)
                else if (parsedPosition == null) Text(stringResource(R.string.next_role_position_integer),
                    style = Zapara.typography.caption, color = Zapara.colors.warn)
                else if (parsedPosition !in 0..maxPosition) Text(stringResource(R.string.next_role_position_range, maxPosition),
                    style = Zapara.typography.caption, color = Zapara.colors.warn)
                ZButton(uiText(R.string.space_day_84), { dispatch(onEvent, GroupSpaceAction.Role(role.copy(name = name.trim(), icon = icon, position = position.toInt(), revision = initialRevision), state.communityId)) }, enabled = canSettings && !state.channelBusy && role.revision == initialRevision && name.trim().length >= 2 && (position.toIntOrNull()?.let { it in 0..maxPosition } == true))
                ZButton(uiText(R.string.space_day_87), { delete = true; dispatch(onEvent, GroupSpaceAction.RoleImpact(role.roleId)) }, enabled = canSettings && !state.channelBusy, ghost = true)
            }
            "powers" -> {
                settingReason?.let { Text(uiText(it), style = Zapara.typography.caption, color = Zapara.colors.warn) }
                val powers = state.space?.capabilities?.powers ?: desk.capabilities.powers
                powers.forEach { power ->
                    val on = desk.powers.any { it.roleId == role.roleId && it.power == power }
                    val allowed = canSettings && (desk.headman || power in desk.mine)
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(on, { dispatch(onEvent, GroupSpaceAction.Power(role.roleId, power, it)) }, enabled = allowed && !state.channelBusy)
                        Text(powerTitle(power), style = Zapara.typography.body)
                    }
                }
                if (!desk.headman && powers.any { it !in desk.mine })
                    Text(uiText(R.string.group_roles_power_unavailable), style = Zapara.typography.caption)
            }
            "people" -> {
                val grants = desk.grants
                val limit = state.space?.capabilities?.maxRolesPerMember ?: 3
                Text(uiText(R.string.space_day_86, limit.toString()), style = Zapara.typography.section)
                grantReason?.let { Text(uiText(it), style = Zapara.typography.caption, color = Zapara.colors.warn) }
                Field(uiText(R.string.group_roles_person_search), query, { query = it; peopleLimit = 40 })
                ZChip(uiText(R.string.group_roles_assigned_only), selected = assignedOnly,
                    onClick = { assignedOnly = !assignedOnly; peopleLimit = 40 })
                val filtered = state.people.filter { person ->
                    (!assignedOnly || grants.any { it.userId == person.id && it.roleId == role.roleId }) &&
                        (query.isBlank() || person.name.contains(query, true) || person.handle.contains(query, true))
                }
                Text(uiText(R.string.group_roles_shown, rolePeopleShown(filtered.size, peopleLimit), filtered.size), style = Zapara.typography.caption)
                filtered.take(peopleLimit).forEach { person ->
                    val on = grants.any { it.roleId == role.roleId && it.userId == person.id }
                    val count = grants.count { it.userId == person.id }
                    val reason = RoleManagementPolicy.personReason(desk, state.people, person)
                        ?: if (!on && count >= limit) R.string.group_roles_limit else null
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(on, { dispatch(onEvent, GroupSpaceAction.Grant(role.roleId, person.id, it)) }, enabled = canGrant && reason == null && !state.channelBusy)
                        Text(uiText(R.string.group_roles_person_count, person.name, officialRoleTitle(person.role), count, limit), style = Zapara.typography.body)
                    }
                    reason?.let { Text(uiText(it), style = Zapara.typography.caption, color = Zapara.colors.text2) }
                }
                if (filtered.size > peopleLimit) ZButton(uiText(R.string.space_day_load_more),
                    { peopleLimit += 40 }, ghost = true, tag = "Group.RolePeopleMore")
                if (filtered.isEmpty()) Text(uiText(R.string.group_roles_people_empty), style = Zapara.typography.caption)
            }
        }
    }
    if (delete) AlertDialog(onDismissRequest = { delete = false }, title = { Text(uiText(R.string.space_day_88, (role.name).toString())) },
        text = { Text(uiText(R.string.space_day_89, (state.roleImpact?.takeIf { it.roleId == role.roleId }?.assignments ?: 0).toString())); Text(uiText(R.string.space_day_role_rules, (state.roleImpact?.takeIf { it.roleId == role.roleId }?.accessRules ?: 0).toString())) },
        confirmButton = { ZButton(uiText(R.string.space_day_90), { dispatch(onEvent, GroupSpaceAction.DeleteRole(role.roleId)); delete = false }, enabled = !state.channelBusy && state.roleImpact?.roleId == role.roleId) }, dismissButton = { ZButton(uiText(R.string.space_day_91), { delete = false }, ghost = true) })
}

@Composable private fun officialRoleTitle(role: String): String = stringResource(when(role) { "headman" -> R.string.group_role_headman; "curator" -> R.string.group_role_curator; else -> R.string.group_role_member })

fun powerResource(power: String): Int = when(power) {
    "read" -> R.string.space_day_92
    "post" -> R.string.space_day_93
    "media" -> R.string.space_day_94
    "vote" -> R.string.space_day_95
    "formsRespond" -> R.string.space_day_96
    "ballots" -> R.string.space_day_97
    "forms" -> R.string.space_day_98
    "close" -> R.string.space_day_99
    "pin" -> R.string.space_day_100
    "moderate" -> R.string.space_day_101
    "homework" -> R.string.space_day_102
    "mentionAll" -> R.string.space_day_103
    "joins" -> R.string.space_day_104
    "exclude" -> R.string.space_day_105
    "channels" -> R.string.space_day_106
    "access" -> R.string.space_day_107
    "roles" -> R.string.space_day_108
    "grants" -> R.string.space_day_109
    else -> R.string.review_unknown_power
}
@Composable fun powerTitle(power: String): String = stringResource(powerResource(power))

@Composable private fun AccessEditor(access: GroupTopicAccess, state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    var roleId by rememberSaveable(access.topicId) { mutableStateOf<String?>(null) }
    var rules by remember(access.topicId) { mutableStateOf(GroupAccessPresets.topicRules(access.rules)) }
    var revisions by rememberSaveable(access.topicId, stateSaver = RevisionGuard.Saver) { mutableStateOf(RevisionGuard(access.revision)) }
    LaunchedEffect(access.revision) { revisions = revisions.observed(access.revision) }
    var advanced by rememberSaveable(access.topicId) { mutableStateOf(false) }
    var affectedQuery by rememberSaveable(access.topicId) { mutableStateOf("") }
    var changedOnly by rememberSaveable(access.topicId) { mutableStateOf(false) }
    var readChoice by rememberSaveable(access.topicId) { mutableStateOf<String?>(null) }
    var postChoice by rememberSaveable(access.topicId) { mutableStateOf<String?>(null) }
    fun change(power: String, value: String) {
        if(power=="read") readChoice=null else if(power=="post") postChoice=null
        rules = rules.filterNot { it.roleId == roleId && it.power == power } + GroupAccessRule(roleId, power, value)
    }
    val approval = state.accessApproval?.takeIf { it.matches(access.topicId, revisions.base, rules) }
    ZCard(Modifier.fillMaxWidth()) {
        if (revisions.conflict) {
            Text(uiText(R.string.review_access_conflict), color = Zapara.colors.warn)
            access.rules.forEach { current -> Text("${current.roleId?.let { id -> state.desk?.roles?.firstOrNull { it.roleId == id }?.name } ?: uiText(R.string.space_day_111)} · ${powerTitle(current.power)} · ${uiText(when(current.state) { "allow" -> R.string.space_day_113; "deny" -> R.string.space_day_114; else -> R.string.space_day_112 })}", style = Zapara.typography.caption) }
            ZButton(uiText(R.string.space_day_reload_changes), { rules = GroupAccessPresets.topicRules(access.rules); readChoice=null; postChoice=null; revisions = revisions.reload() }, ghost = true)
            ZButton(uiText(R.string.review_keep_draft), { revisions = revisions.reload() }, ghost = true)
        }
        FlowRow { ZChip(uiText(R.string.space_day_111), selected = roleId == null, onClick = { roleId = null }); state.desk?.roles.orEmpty().forEach { role -> ZChip(role.name, selected = roleId == role.roleId, onClick = { roleId = role.roleId }) } }
        Text(uiText(R.string.review_acl_limits),style=Zapara.typography.caption)
        listOf("read", "post").forEach { power ->
            val selection=GroupAccessPresets.editorSelection(rules,power,if(power=="read") readChoice else postChoice)
            Text(powerTitle(power),style=Zapara.typography.section)
            if(selection.mode==GroupAccessPresets.Mode.Custom) Text(uiText(R.string.review_acl_custom),style=Zapara.typography.caption,color=Zapara.colors.warn)
            FlowRow(horizontalArrangement=Arrangement.spacedBy(Zapara.space.xs)) {
                val choices=if(power=="read") listOf(GroupAccessPresets.Mode.All to R.string.review_read_all,GroupAccessPresets.Mode.Selected to R.string.review_selected_roles)
                    else listOf(GroupAccessPresets.Mode.All to R.string.review_post_inherited,GroupAccessPresets.Mode.Selected to R.string.review_selected_roles,GroupAccessPresets.Mode.Headman to R.string.review_headman_only)
                choices.forEach { (mode,label) -> ZChip(uiText(label),selected=selection.mode==mode,onClick={
                    if(power=="read") readChoice=mode.name else postChoice=mode.name
                    rules=GroupAccessPresets.apply(rules,power,GroupAccessPresets.Selection(mode,if(mode==GroupAccessPresets.Mode.Selected) selection.roles else emptySet()))
                }) }
            }
            if(selection.mode==GroupAccessPresets.Mode.Selected) {
                state.desk?.roles.orEmpty().sortedByDescending { it.position }.forEach { role -> Row(verticalAlignment=Alignment.CenterVertically) {
                    Checkbox(role.roleId in selection.roles,{ on -> rules=GroupAccessPresets.apply(rules,power,selection.copy(roles=if(on) selection.roles+role.roleId else selection.roles-role.roleId)) },enabled=!state.channelBusy)
                    Text(role.name,style=Zapara.typography.body)
                } }
                if(selection.roles.isEmpty()) Text(uiText(R.string.review_select_a_role),style=Zapara.typography.caption,color=Zapara.colors.warn)
            }
        }
        ZButton(uiText(R.string.review_advanced_access), { advanced = !advanced }, ghost = true, quiet = true)
        if (advanced) state.space?.capabilities?.powers.orEmpty().filterNot { it in GroupAccessPresets.groupOnly }.forEach { power ->
            Text(powerTitle(power), style = Zapara.typography.bodyStrong)
            val selected = rules.firstOrNull { it.roleId == roleId && it.power == power }?.state ?: "inherit"
            FlowRow { listOf("inherit" to R.string.space_day_112, "allow" to R.string.space_day_113, "deny" to R.string.space_day_114).forEach { (value,label) -> ZChip(uiText(label), selected = value == selected, onClick = { change(power,value) }) } }
        }
        if (rules != GroupAccessPresets.topicRules(access.rules)) ZButton(stringResource(R.string.ux100_chat_access_reset), {
            rules = GroupAccessPresets.topicRules(access.rules); readChoice = null; postChoice = null; revisions = revisions.reload()
        }, enabled = !state.channelBusy, ghost = true)
        ZButton(uiText(R.string.review_reload_access), { readChoice=null; postChoice=null; dispatch(onEvent,GroupSpaceAction.LoadAccess(access.topicId)) },enabled=!state.channelBusy,ghost=true)
        ZButton(uiText(R.string.review_access_preview), { dispatch(onEvent, GroupSpaceAction.PreviewAccess(access.topicId, rules, revisions.base)) }, enabled = !state.channelBusy && !revisions.conflict, busy = state.channelBusy)
        if (approval != null) {
            Text(uiText(R.string.review_access_effect, approval.response.affectedCount, approval.response.beforeReaders.size, approval.response.afterReaders.size, approval.addedReaders.size, approval.removedReaders.size), style = Zapara.typography.body)
            val ordinary = approval.response.participants.firstOrNull { p -> state.people.firstOrNull { it.id == p.userId }?.role == "member" && state.desk?.grants.orEmpty().none { it.userId == p.userId } }
            if (ordinary == null) Text(uiText(R.string.review_no_ordinary_member), style = Zapara.typography.caption)
            else Text(uiText(R.string.review_ordinary_member, ordinary.afterPermissions.joinToString { power -> uiText(powerResource(power)) }), style = Zapara.typography.caption)
            Field(stringResource(R.string.ux100_chat_affected_search), affectedQuery, { affectedQuery = it })
            ZChip(stringResource(R.string.ux100_chat_affected_changed), selected = changedOnly, onClick = { changedOnly = !changedOnly })
            val shownParticipants = approval.response.participants.filter { person ->
                val member = state.people.firstOrNull { it.id == person.userId }
                (affectedQuery.isBlank() || listOf(member?.name.orEmpty(), member?.handle.orEmpty()).any { it.contains(affectedQuery.trim(), true) }) &&
                    (!changedOnly || person.beforePermissions.toSet() != person.afterPermissions.toSet())
            }
            Text(stringResource(R.string.ux100_chat_results, shownParticipants.size, approval.response.participants.size), style = Zapara.typography.caption)
            shownParticipants.forEach { person ->
                var details by rememberSaveable(access.topicId, person.userId) { mutableStateOf(false) }
                val name = state.people.firstOrNull { it.id == person.userId }?.name ?: uiText(R.string.space_day_148)
                ZButton(name, { details = !details }, ghost = true, quiet = true)
                if (details) state.space?.capabilities?.powers.orEmpty().forEach { power ->
                    val source = person.sources[power].orEmpty().let { original -> state.desk?.roles.orEmpty().fold(original) { value, role -> value.replace(role.roleId, role.name, ignoreCase = true) } }
                    Text("${powerTitle(power)}: ${uiText(if (power in person.afterPermissions) R.string.space_day_113 else R.string.space_day_114)} · $source", style = Zapara.typography.caption)
                }
            }
            Text(uiText(R.string.review_access_confirmation), style = Zapara.typography.caption, color = Zapara.colors.warn)
            ZButton(uiText(R.string.review_access_save, approval.response.affectedCount), { dispatch(onEvent, GroupSpaceAction.Access(access.topicId, rules, revisions.base)) }, enabled = !state.channelBusy && !revisions.conflict)
        }
    }
}

@Composable fun SpecializedChannel(state: GroupUiState, onEvent: (GroupEvent) -> Unit, modifier: Modifier) {
    val uiText = rememberUiText()
    val nativeContext = LocalContext.current
    val calendarTheme = if (Zapara.colors.isDark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light
    val topic = GroupActions.topic(state, state.activeTopicId)
    val permissions = topic?.permissions.orEmpty()
    var completedFilter by rememberSaveable(state.activeTopicId) { mutableStateOf(false) }
    var formQuery by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var formFilter by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("all") }
    LazyColumn(modifier, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item { state.spaceError?.let { Text(it, color = Zapara.colors.bad) }; if (state.channelBusy) LinearProgressIndicator(Modifier.fillMaxWidth()); ZButton(uiText(R.string.space_day_116), { dispatch(onEvent, GroupSpaceAction.ReloadContent) }, enabled = !state.channelBusy, ghost = true) }
        when(state.activeChannelKind) {
            "forms" -> {
                if ("forms" in permissions && state.preview == null) item { FormBuilder(state, onEvent) }
                val shown = state.forms.filter { form ->
                    (formQuery.isBlank() || (form.title + " " + form.description).contains(formQuery.trim(), ignoreCase = true)) &&
                        when (formFilter) { "unanswered" -> form.ownResponse == null && form.canRespond
                            "answered" -> form.ownResponse != null; "closed" -> !form.canRespond; else -> true }
                }
                item {
                    Field(stringResource(R.string.ux100_chat_form_search), formQuery, { formQuery = it })
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        listOf("all" to R.string.ux100_chat_form_all, "unanswered" to R.string.ux100_chat_form_unanswered,
                            "answered" to R.string.ux100_chat_form_answered, "closed" to R.string.ux100_chat_form_closed).forEach { (value, label) ->
                            ZChip(stringResource(label), selected = formFilter == value, onClick = { formFilter = value })
                        }
                    }
                    Text(stringResource(R.string.ux100_chat_results, shown.size, state.forms.size), style = Zapara.typography.caption)
                    if (shown.isEmpty()) Text(stringResource(R.string.ux100_chat_filter_empty), style = Zapara.typography.body)
                    if (formQuery.isNotBlank() || formFilter != "all") ZButton(stringResource(R.string.ux100_chat_reset), { formQuery = ""; formFilter = "all" }, ghost = true)
                }
                items(shown, key = { it.formId }) { FormCard(it, state, onEvent) }
            }
            "homework" -> {
                if ("homework" in permissions && state.preview == null) item { SharedHomeworkEditor(state, onEvent) }
                val finished = state.homework.filter { state.completions[it.homeworkId]?.completed == true }
                val active = state.homework.filterNot { state.completions[it.homeworkId]?.completed == true }
                item {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                        ZChip(uiText(R.string.group_homework_active_filter, active.size), selected = !completedFilter, onClick = { completedFilter = false })
                        ZChip(uiText(R.string.group_homework_done_filter, finished.size), selected = completedFilter, onClick = { completedFilter = true })
                    }
                }
                val shown = if (completedFilter) finished else active
                items(shown, key = { it.homeworkId }) { homework -> ZCard(Modifier.fillMaxWidth()) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(state.completions[homework.homeworkId]?.completed == true, { dispatch(onEvent, GroupSpaceAction.CompleteHomework(homework.homeworkId, it)) }, enabled = !state.channelBusy && state.preview == null && (state.space?.capabilities?.homeworkAudience != true || homework.canComplete))
                        Text(homework.title, style = Zapara.typography.bodyStrong)
                    }
                    Text(homework.body, style = Zapara.typography.body); Text(uiText(R.string.group_homework_due, displayDeadline(homework.deadlineAt)), style = Zapara.typography.caption)
                    Text(uiText(if (homework.audience?.selected == true) R.string.group_homework_selected else R.string.group_homework_all), style = Zapara.typography.caption)
                    Text(uiText(if (homework.canComplete || state.space?.capabilities?.homeworkAudience != true) R.string.group_homework_my_done else R.string.group_homework_manager_only), style = Zapara.typography.caption)
                    Text(uiText(R.string.space_day_118), style = Zapara.typography.caption)
                    ZButton(uiText(R.string.space_day_22), { onEvent(GroupEvent.Discuss("${homework.title} · ${homework.deadlineAt?.atZone(ZoneId.systemDefault())?.toLocalDate() ?: uiText(R.string.space_day_61)} · ${homework.body}")) }, ghost = true, enabled = state.preview == null)
                    if ("homework" in permissions && state.preview == null && (state.space?.capabilities?.homeworkAudience != true || homework.canEdit)) {
                        var editing by rememberSaveable(homework.homeworkId) { mutableStateOf(false) }
                        ZButton(uiText(R.string.space_day_edit_homework), { editing = !editing }, ghost = true)
                        if (editing) SharedHomeworkEditor(state, onEvent, homework)
                    }

                } }
                if (shown.isEmpty()) item { Text(uiText(R.string.group_homework_filter_empty), style = Zapara.typography.caption) }
            }
            "schedule" -> {
                item {
                    FlowRow(horizontalArrangement=Arrangement.spacedBy(Zapara.space.xs)) {
                        listOf(R.string.space_day_1,R.string.space_day_2,R.string.space_day_3).forEachIndexed { offset,label ->
                            val day=LocalDate.now().plusDays(offset.toLong())
                            ZButton(uiText(label),{ dispatch(onEvent,GroupSpaceAction.ScheduleDate(day)) },ghost=state.scheduleDate!=day)
                        }
                        ZButton(uiText(R.string.space_day_5),{
                            val date=state.scheduleDate
                            DatePickerDialog(nativeContext, calendarTheme,
                                { _,year,month,day -> dispatch(onEvent,GroupSpaceAction.ScheduleDate(LocalDate.of(year,month+1,day))) },date.year,date.monthValue-1,date.dayOfMonth).show()
                        },ghost=true)
                    }
                    Row { ZButton(uiText(R.string.space_day_119), { dispatch(onEvent, GroupSpaceAction.ScheduleDate(state.scheduleDate.minusDays(1))) }, ghost = true); ZButton(uiText(R.string.space_day_120), { dispatch(onEvent, GroupSpaceAction.ScheduleDate(state.scheduleDate.plusDays(1))) }, ghost = true) }
                    Text(state.scheduleDate.toString(), style = Zapara.typography.section) }
                items(state.scheduleRows) { Text(it, style = Zapara.typography.body) }
                if (state.scheduleRows.isEmpty()) item { Text(uiText(R.string.space_day_121), style = Zapara.typography.body) }
            }
            else -> item { Text(uiText(R.string.space_day_122), style = Zapara.typography.body) }
        }
    }
}

@Composable private fun FormBuilder(state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    var preview by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var discard by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var open by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var title by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var description by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var deadline by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf("") }
    var anonymous by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf(false) }
    var questions by rememberSaveable(state.communityId, state.activeTopicId, stateSaver = questionSaver) { mutableStateOf(listOf(GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList()))) }
    var deletingQuestion by rememberSaveable(state.communityId, state.activeTopicId) { mutableStateOf<String?>(null) }
    val initialVersion = remember(state.communityId, state.activeTopicId) { state.formCreateVersion }
    LaunchedEffect(state.formCreateVersion) { if (state.formCreateVersion > initialVersion) { open = false; title = ""; description = ""; deadline = ""; questions = listOf(GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList())) } }
    ZCard(Modifier.fillMaxWidth()) {
        ZButton(uiText(R.string.space_day_123), { open = !open }, enabled = !state.channelBusy, ghost = true)
        if (open) {
            Field(uiText(R.string.space_day_124), title, { title = it.take(120) }, !state.channelBusy)
            Field(uiText(R.string.space_day_125), description, { description = it.take(2000) }, !state.channelBusy)
            FormDeadlineField(deadline, { deadline = it }, !state.channelBusy)
            Text(stringResource(R.string.ux100_chat_question_count, questions.size), style = Zapara.typography.caption)
            Row(verticalAlignment = Alignment.CenterVertically) { Switch(anonymous, { anonymous = it }, enabled = !state.channelBusy); Text(uiText(R.string.space_day_127)) }
            questions.forEachIndexed { index, question -> key(question.questionId) {
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    ZButton(stringResource(R.string.next_question_up), { questions = moveQuestion(questions, index, -1) },
                        enabled = index > 0 && !state.channelBusy, ghost = true)
                    ZButton(stringResource(R.string.next_question_down), { questions = moveQuestion(questions, index, 1) },
                        enabled = index < questions.lastIndex && !state.channelBusy, ghost = true)
                }
                Field(uiText(R.string.space_day_128, (index + 1).toString()), question.title, { value -> questions = questions.mapIndexed { i, q -> if (i == index) q.copy(title = value.take(400)) else q } }, !state.channelBusy)
                FlowRow { listOf("shortText" to uiText(R.string.space_day_129), "longText" to uiText(R.string.space_day_130), "singleChoice" to uiText(R.string.space_day_131), "multipleChoice" to uiText(R.string.space_day_132)).forEach { (kind, label) -> ZChip(label, selected = question.kind == kind, onClick = { if (!state.channelBusy) questions = questions.mapIndexed { i, q -> if (i == index) q.copy(kind = kind) else q } }) } }
                Row(verticalAlignment = Alignment.CenterVertically) { Checkbox(question.required, { value -> questions = questions.mapIndexed { i, q -> if (i == index) q.copy(required = value) else q } }, enabled = !state.channelBusy); Text(uiText(R.string.space_day_133)) }
                if (question.kind.endsWith("Choice")) Field(uiText(R.string.space_day_134), question.options.joinToString("\n"), { value -> questions = questions.mapIndexed { i, q -> if (i == index) q.copy(options = value.split("\n")) else q } }, !state.channelBusy)
                val questionProblem = GroupFormDraft(uiText(R.string.ux100_chat_question_validation), "", null, false, listOf(question)).problem(Instant.now())
                if (questionProblem != null) Text(stringResource(if (questionProblem == "options") R.string.ux100_chat_question_problem else R.string.ux100_chat_question_text_problem, index + 1),
                    style = Zapara.typography.caption, color = Zapara.colors.warn)
                ZButton(stringResource(R.string.ux100_chat_duplicate_question), {
                    questions = duplicateFormQuestion(questions, question.questionId, UUID.randomUUID().toString())
                }, enabled = questions.size < 30 && !state.channelBusy, ghost = true)
                if (questions.size > 1) ZButton(uiText(R.string.space_day_135), {
                    if (questionHasDraft(question)) deletingQuestion = question.questionId
                    else questions = questions.filterNot { it.questionId == question.questionId }
                }, enabled = !state.channelBusy, ghost = true)
            }
            }
            ZButton(uiText(R.string.space_day_136), { questions = questions + GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList()) }, enabled = questions.size < 30 && !state.channelBusy, ghost = true)
            val parsed = runCatching { parseDeadline(deadline) }
            val payload = GroupFormDraft(title, description, parsed.getOrNull(), anonymous, questions).normalized()
            val problem = if (parsed.isFailure) "deadline" else payload.problem(Instant.now())
            if (problem != null) Text(uiText(when(problem) {
                "title" -> R.string.review_form_title; "description" -> R.string.review_form_description; "deadline" -> R.string.review_form_deadline
                "questions" -> R.string.review_form_questions; "question" -> R.string.review_form_question; "options" -> R.string.review_form_options; else -> R.string.review_form_size
            }), style = Zapara.typography.caption, color = Zapara.colors.warn)
            val valid = problem == null
            FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.ux100_chat_preview), { preview = true }, enabled = valid && !state.channelBusy, ghost = true)
                ZButton(stringResource(R.string.ux100_chat_discard_form), { discard = true }, enabled = !state.channelBusy, ghost = true)
            }
            ZButton(uiText(R.string.space_day_137), { dispatch(onEvent, GroupSpaceAction.CreateForm(title, description, parseDeadline(deadline), anonymous, payload.questions)) }, enabled = valid && !state.channelBusy, busy = state.channelBusy)
            Text(uiText(R.string.space_day_138), style = Zapara.typography.caption)
        }
    }
    if (preview) ZBottomSheet(onDismiss = { preview = false }, tag = "Group.FormPreview", scrollable = true) {
        Text(title, style = Zapara.typography.section)
        Text(description, style = Zapara.typography.body)
        Text(deadline.ifBlank { uiText(R.string.space_day_61) }, style = Zapara.typography.caption)
        Text(uiText(if (anonymous) R.string.review_form_anonymous_audience else R.string.review_form_named_audience), style = Zapara.typography.caption)
        questions.forEachIndexed { index, question ->
            Text("${index + 1}. ${question.title}", style = Zapara.typography.bodyStrong)
            Text(stringResource(if (question.required) R.string.ux100_chat_form_question_required else R.string.ux100_chat_form_question_optional), style = Zapara.typography.caption)
            question.options.filter(String::isNotBlank).forEach { Text("○ $it", style = Zapara.typography.body) }
        }
        ZButton(stringResource(R.string.ux100_chat_preview_close), { preview = false })
    }
    if (discard) AlertDialog(onDismissRequest = { discard = false },
        title = { Text(stringResource(R.string.ux100_chat_discard_title)) },
        text = { Text(stringResource(R.string.ux100_chat_discard_body)) },
        confirmButton = { Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.ux100_chat_discard_form), {
            title = ""; description = ""; deadline = ""; anonymous = false
            questions = listOf(GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList()))
            discard = false
            }, modifier = Modifier.fillMaxWidth(), enabled = !state.channelBusy)
            ZButton(uiText(R.string.channel_cancel), { discard = false }, modifier = Modifier.fillMaxWidth(), ghost = true)
        } })
    val target = questions.firstOrNull { it.questionId == deletingQuestion }
    if (target != null) AlertDialog(onDismissRequest = { deletingQuestion = null },
        title = { Text(stringResource(R.string.next_question_delete_title)) },
        text = { Text(stringResource(R.string.next_question_delete_body, target.title.ifBlank { stringResource(R.string.next_question_untitled) })) },
        confirmButton = { ZButton(stringResource(R.string.next_question_delete), {
            if (questions.size > 1 && questions.any { it.questionId == target.questionId })
                questions = questions.filterNot { it.questionId == target.questionId }
            deletingQuestion = null
        }, enabled = !state.channelBusy) },
        dismissButton = { ZButton(stringResource(R.string.channel_cancel), { deletingQuestion = null }, ghost = true) })
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable private fun FormCard(form: GroupForm, state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    var answers by rememberSaveable(form.formId, stateSaver = answerSaver) { mutableStateOf(form.ownResponse?.answers ?: emptyList()) }
    fun set(answer: GroupFormAnswer) { answers = answers.filterNot { it.questionId == answer.questionId } + answer }
    val missing = missingRequiredQuestions(form.questions, answers)
    val tooLong = form.questions.any { question ->
        val limit = if (question.kind == "shortText") 500 else 8000
        question.kind in setOf("shortText", "longText") && (answers.firstOrNull { it.questionId == question.questionId }?.text?.length ?: 0) > limit
    }
    val valid = missing.isEmpty() && !tooLong
    val dirty = !sameFormAnswers(answers, form.ownResponse?.answers.orEmpty())
    var restoring by rememberSaveable(form.formId) { mutableStateOf(false) }
    var showResponses by rememberSaveable(form.formId) { mutableStateOf(false) }
    val requiredCount = form.questions.count { it.required }
    val requesters = remember(form.formId, form.questions.map { it.questionId }) {
        form.questions.associate { it.questionId to BringIntoViewRequester() }
    }
    val scope = rememberCoroutineScope()
    ZCard(Modifier.fillMaxWidth()) {
        Text(form.title, style = Zapara.typography.section); Text(form.description, style = Zapara.typography.body)
        Text(uiText(R.string.space_day_141, (displayDeadline(form.deadlineAt)).toString(), (if (form.anonymous) uiText(R.string.space_day_139) else uiText(R.string.space_day_140)).toString(), (form.responseCount).toString()), style = Zapara.typography.caption)
        Text(uiText(if (form.anonymous) R.string.review_form_anonymous_audience else R.string.review_form_named_audience), style = Zapara.typography.caption)
        form.ownResponse?.let { response ->
            val savedAt = response.updatedAt.atZone(ZoneId.systemDefault())
                .format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm"))
            Text(stringResource(R.string.ux60_chat_form_saved, savedAt),
                style = Zapara.typography.caption, color = Zapara.colors.ok,
                modifier = Modifier.testTag("Group.FormSaved.${form.formId}"))
        }
        if (form.canRespond && (dirty || form.ownResponse != null)) Text(stringResource(if (dirty) R.string.ux100_chat_answer_dirty else R.string.ux100_chat_form_saved_clean),
            style = Zapara.typography.caption, color = if (dirty) Zapara.colors.warn else Zapara.colors.ok)
        if (form.canRespond && dirty) ZButton(stringResource(R.string.ux100_chat_restore_answers), { restoring = true }, enabled = !state.channelBusy && state.preview == null, ghost = true)
        if (form.canRespond) Text(stringResource(R.string.ux30_form_progress, requiredCount - missing.size, requiredCount),
            style = Zapara.typography.caption, color = Zapara.colors.text2)
        form.questions.forEach { question ->
            val answer = answers.firstOrNull { it.questionId == question.questionId } ?: GroupFormAnswer(question.questionId, null)
            Text(question.title + if (question.required) " *" else "", style = Zapara.typography.bodyStrong,
                modifier = Modifier.bringIntoViewRequester(requesters.getValue(question.questionId)))
            if (question.kind in setOf("shortText", "longText")) {
                val limit = if (question.kind == "shortText") 500 else 8000
                ZTextField(answer.text.orEmpty(), { set(answer.copy(text = it)) }, label = { Text(uiText(R.string.space_day_142)) },
                    enabled = form.canRespond && !state.channelBusy && state.preview == null,
                    modifier = Modifier.fillMaxWidth(), minLines = if (question.kind == "longText") 3 else 1, maxLines = 8,
                    isError = answer.text.orEmpty().length > limit,
                    supportingText = { Text(stringResource(R.string.ux100_chat_answer_length, answer.text.orEmpty().length, limit)) })
            }
            else question.options.forEach { option -> Row(verticalAlignment = Alignment.CenterVertically) {
                if (question.kind == "singleChoice") RadioButton(option in answer.choices, { set(answer.copy(choices = listOf(option))) }, enabled = form.canRespond && !state.channelBusy && state.preview == null)
                else Checkbox(option in answer.choices, { on -> set(answer.copy(choices = if (on) answer.choices + option else answer.choices - option)) }, enabled = form.canRespond && !state.channelBusy && state.preview == null)
                Text(option, style = Zapara.typography.body)
            } }
            if (question.kind == "singleChoice" && !question.required && answer.choices.isNotEmpty())
                ZButton(stringResource(R.string.ux100_chat_clear_choice), { set(answer.copy(choices = emptyList())) },
                    enabled = form.canRespond && !state.channelBusy && state.preview == null, ghost = true)
        }
        if (form.canRespond && missing.isNotEmpty()) {
            Text(stringResource(R.string.ux30_form_missing, missing.size),
                style = Zapara.typography.caption, color = Zapara.colors.warn)
            Text(missing.take(3).joinToString(" · ") { it.title }, style = Zapara.typography.caption,
                color = Zapara.colors.text2)
            ZButton(stringResource(R.string.ux30_form_first_missing),
                { scope.launch { requesters[missing.first().questionId]?.bringIntoView() } }, ghost = true,
                tag = "Group.FormFirstMissing")
        }
        if (form.canRespond) ZButton(if (form.ownResponse == null) uiText(R.string.space_day_143) else uiText(R.string.space_day_144), { dispatch(onEvent, GroupSpaceAction.SubmitForm(form.formId, answers)) }, enabled = valid && (dirty || form.ownResponse == null) && !state.channelBusy && state.preview == null)
        else Text(uiText(R.string.space_day_145), style = Zapara.typography.caption)
        if (form.canViewResponses) ZButton(if (showResponses) stringResource(R.string.ux100_chat_answers_hide) else uiText(R.string.space_day_146), {
            showResponses = !showResponses
            if (showResponses) dispatch(onEvent, GroupSpaceAction.Responses(form.formId))
        }, enabled = !state.channelBusy, ghost = true)
        if (showResponses && form.canViewResponses) {
        state.responseCounts[form.formId]?.let { total -> Text(uiText(R.string.space_day_response_count, state.responses[form.formId].orEmpty().size, total), style = Zapara.typography.caption) }
        state.responseCursors[form.formId]?.let { cursor -> ZButton(uiText(R.string.space_day_load_more), { dispatch(onEvent, GroupSpaceAction.Responses(form.formId, cursor)) }, enabled = !state.channelBusy, ghost = true) }
        state.responses[form.formId]?.forEach { response ->
            Text(if (form.anonymous) uiText(R.string.space_day_147) else state.people.firstOrNull { it.id == response.respondentId }?.name ?: uiText(R.string.space_day_148), style = Zapara.typography.bodyStrong)
            response.answers.forEach { answer -> Text("${form.questions.firstOrNull { it.questionId == answer.questionId }?.title}: ${answer.text?.takeIf { it.isNotEmpty() } ?: answer.choices.joinToString()}", style = Zapara.typography.body) }
        }
        }
    }
    if (restoring) AlertDialog(onDismissRequest = { restoring = false },
        title = { Text(stringResource(R.string.ux100_chat_restore_title)) },
        text = { Text(stringResource(R.string.ux100_chat_restore_body)) },
        confirmButton = { ZButton(stringResource(R.string.ux100_chat_restore), {
            answers = form.ownResponse?.answers.orEmpty(); restoring = false
        }, enabled = !state.channelBusy && state.preview == null) },
        dismissButton = { ZButton(uiText(R.string.channel_cancel), { restoring = false }, ghost = true) })
}

@Composable private fun SharedHomeworkEditor(state: GroupUiState, onEvent: (GroupEvent) -> Unit, initial: CommunityHomework? = null) {
    val uiText = rememberUiText()
    val editorScope = GroupHomeworkDraftRules.scope(state.communityId, state.activeTopicId, initial?.homeworkId)
    var title by rememberSaveable(editorScope) { mutableStateOf(initial?.title ?: "") }; var body by rememberSaveable(editorScope) { mutableStateOf(initial?.body ?: "") }; var deadline by rememberSaveable(editorScope) { mutableStateOf(initial?.deadlineAt?.atZone(ZoneId.systemDefault())?.toLocalDate()?.toString() ?: "") }
    var revisions by rememberSaveable(editorScope, stateSaver = RevisionGuard.Saver) { mutableStateOf(RevisionGuard(initial?.revision ?: 0)) }
    var audience by rememberSaveable(editorScope, stateSaver = GroupHomeworkDraftRules.AudienceSaver) { mutableStateOf(initial?.audience ?: HomeworkAudience()) }
    var operationId by rememberSaveable(editorScope) { mutableStateOf(UUID.randomUUID().toString()) }
    val targetedSupported = state.space?.capabilities?.homeworkAudience == true
    LaunchedEffect(initial?.revision) {
        initial?.let { current ->
            revisions = revisions.observed(current.revision)
        }
    }
    LaunchedEffect(state.lastSavedHomework?.homeworkId, state.lastSavedHomework?.revision) {
        state.lastSavedHomework?.takeIf { it.homeworkId == initial?.homeworkId }?.let { saved ->
            revisions = revisions.observed(initial?.revision ?: saved.revision).acknowledgedOwn(saved.revision)
        }
    }
    val initialVersion = remember(editorScope) { state.homeworkCreateVersion }
    LaunchedEffect(state.homeworkCreateVersion, state.lastCreatedHomeworkOperationId) {
        if (initial == null && state.homeworkCreateVersion > initialVersion && state.lastCreatedHomeworkOperationId == operationId) {
            title = ""; body = ""; deadline = ""; audience = HomeworkAudience()
            operationId = GroupHomeworkDraftRules.nextOperation(operationId, state.lastCreatedHomeworkOperationId) { UUID.randomUUID().toString() }
        }
    }
    ZCard(Modifier.fillMaxWidth()) {
        if (initial != null && revisions.conflict) {
            val audienceConflict = GroupHomeworkDraftRules.conflict(audience, initial.audience)
            Text(uiText(R.string.review_homework_conflict), color = Zapara.colors.warn)
            Text("${initial.title} · ${initial.body} · ${displayDeadline(initial.deadlineAt)} · ${initial.revision}", style = Zapara.typography.caption)
            Text(uiText(R.string.group_homework_server_audience, homeworkAudienceDescription(audienceConflict.server, state)), style = Zapara.typography.caption)
            Text(uiText(R.string.group_homework_draft_audience, homeworkAudienceDescription(audienceConflict.draft, state)), style = Zapara.typography.caption)
            ZButton(uiText(R.string.space_day_reload_changes), { title = initial.title; body = initial.body; deadline = displayDeadlineValue(initial.deadlineAt); audience = audienceConflict.reloadAudience(); revisions = revisions.reload() }, ghost = true)
            ZButton(uiText(R.string.review_keep_draft), { audience = audienceConflict.keepDraftAudience(); revisions = revisions.reload() }, ghost = true)
        }
        Field(uiText(R.string.space_day_149), title, { title = it }, !state.channelBusy); Field(uiText(R.string.space_day_150), body, { body = it }, !state.channelBusy); Field(uiText(R.string.space_day_151), deadline, { deadline = it }, !state.channelBusy)
        Text(uiText(R.string.group_homework_editor_context, state.title, GroupActions.topic(state, state.activeTopicId)?.title ?: uiText(R.string.group_homework_no_topic), deadline.ifBlank { uiText(R.string.group_homework_no_deadline) }), style = Zapara.typography.caption)
        if (targetedSupported) HomeworkAudiencePicker(editorScope, audience,
            state.desk?.roles.orEmpty().map { AudienceChoice(it.roleId, it.name) },
            state.people.filterNot { it.self }.map { AudienceChoice(it.id, it.name) }, !state.channelBusy, { audience = it })
        else Text(uiText(R.string.group_homework_old_server), style = Zapara.typography.caption)
        Text(uiText(R.string.group_homework_text_only), style = Zapara.typography.caption)
        ZButton(if (initial == null) uiText(R.string.space_day_152) else uiText(R.string.space_day_save_homework), { dispatch(onEvent, GroupSpaceAction.SaveHomework(initial?.homeworkId, title, body, revisions.base, if (initial != null && displayDeadlineValue(initial.deadlineAt) == deadline) initial.deadlineAt else parseDeadline(deadline), audience, operationId.takeIf { initial == null })) }, enabled = !state.channelBusy && !revisions.conflict && title.trim().length >= 2 && body.isNotBlank() && runCatching { parseDeadline(deadline) }.isSuccess && (!audience.selected || targetedSupported) && (!targetedSupported || audience.valid())) }
}

@Composable private fun homeworkAudienceDescription(value: HomeworkAudience, state: GroupUiState): String {
    val uiText = rememberUiText()
    if (!value.selected) return uiText(R.string.homework_audience_all)
    val roles = value.roleIds.map { id -> state.desk?.roles?.firstOrNull { it.roleId == id }?.name ?: id }
    val people = value.userIds.map { id -> state.people.firstOrNull { it.id == id }?.name ?: id }
    return uiText(R.string.homework_audience_selected) + ": " + (roles + people).joinToString(", ")
}

@Composable private fun FormDeadlineField(value: String, change: (String) -> Unit, enabled: Boolean) {
    val context = LocalContext.current
    val theme = if (Zapara.colors.isDark) R.style.Zapara_DatePicker_Dark else R.style.Zapara_DatePicker_Light
    Field(stringResource(R.string.space_day_126), value, change, enabled)
    FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        ZButton(stringResource(R.string.ux100_chat_calendar), {
            val date = runCatching { LocalDate.parse(value) }.getOrDefault(LocalDate.now().plusDays(1))
            DatePickerDialog(context, theme, { _, year, month, day -> change(LocalDate.of(year, month + 1, day).toString()) },
                date.year, date.monthValue - 1, date.dayOfMonth).show()
        }, enabled = enabled, ghost = true)
        ZButton(stringResource(R.string.ux100_chat_tomorrow), { change(LocalDate.now().plusDays(1).toString()) }, enabled = enabled, ghost = true)
        ZButton(stringResource(R.string.ux100_chat_week), { change(LocalDate.now().plusDays(7).toString()) }, enabled = enabled, ghost = true)
        if (value.isNotBlank()) ZButton(stringResource(R.string.ux100_chat_no_deadline), { change("") }, enabled = enabled, ghost = true)
    }
}
