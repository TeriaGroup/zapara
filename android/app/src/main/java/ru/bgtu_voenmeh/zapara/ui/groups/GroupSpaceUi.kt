@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.saveable.Saver
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
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
    val close = { dispatch(onEvent, GroupSpaceAction.Panel(null)) }
    BackHandler { close() }
    ZBottomSheet(onDismiss = close, tag = "Group.Space.$panel") {
        Text(when(panel) { "roles" -> uiText(R.string.space_day_62); "categories" -> uiText(R.string.space_day_63); "archive" -> uiText(R.string.space_day_64); "access" -> uiText(R.string.space_day_65); "preview" -> uiText(R.string.space_day_66); else -> uiText(R.string.space_day_67) }, style = Zapara.typography.section)
        state.spaceError?.let { Text(it, color = Zapara.colors.bad, style = Zapara.typography.body) }
        if (state.channelBusy) LinearProgressIndicator(Modifier.fillMaxWidth())
        LazyColumn(Modifier.fillMaxWidth().heightIn(max = 600.dp), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            when(panel) {
                "categories" -> {
                    item { CategoryEditor(null, state, onEvent) }
                    items(state.space?.categories.orEmpty().sortedBy { it.position }, key = { it.categoryId }) { CategoryEditor(it, state, onEvent) }
                }
                "archive" -> items(state.archived, key = { it.topicId ?: "general" }) { topic ->
                    ZCard(Modifier.fillMaxWidth(),onClick={ topic.topicId?.let { onEvent(GroupEvent.OpenArchived(it)) } }) { Text(topic.title, style = Zapara.typography.bodyStrong); Text(topic.description, style = Zapara.typography.caption)
                        ZButton(uiText(R.string.space_day_68), { dispatch(onEvent, GroupSpaceAction.Archive(topic, false)) }, enabled = !state.channelBusy && GroupActions.canManageTopic(state,topic), ghost = true) }
                }
                "roles" -> {
                    item {
                        val roles = state.desk?.roles.orEmpty().sortedByDescending { it.position }
                        var selectedRoleId by rememberSaveable(state.communityId) { mutableStateOf<String?>(null) }
                        var name by rememberSaveable(state.communityId) { mutableStateOf("") }
                        val selected = roles.firstOrNull { it.roleId == selectedRoleId }
                        val previousIds = remember(state.communityId) { mutableStateOf(roles.map { it.roleId }.toSet()) }
                        LaunchedEffect(roles.map { it.roleId }) {
                            if (selectedRoleId != null && selected == null) selectedRoleId = null
                            val added = roles.firstOrNull { it.roleId !in previousIds.value }
                            if (added != null && added.name.equals(name.trim(), ignoreCase = true)) { selectedRoleId = added.roleId; name = "" }
                            previousIds.value = roles.map { it.roleId }.toSet()
                        }
                        Text(uiText(R.string.space_day_69), style = Zapara.typography.caption)
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
                                ZButton(uiText(R.string.group_roles_card, role.icon, role.name, role.position, grants), { selectedRoleId = if (selectedRoleId == role.roleId) null else role.roleId }, modifier = Modifier.fillMaxWidth(), ghost = selectedRoleId != role.roleId)
                                Text(powers.take(3).joinToString(" · ").ifBlank { uiText(R.string.group_roles_no_powers) } + if (powers.size > 3) uiText(R.string.group_roles_more_powers, powers.size - 3) else "", style = Zapara.typography.caption)
                            }
                        }
                        if (selected != null) RoleEditor(selected, state, onEvent)
                    }
                }
                "access" -> state.access?.let { access -> item { AccessEditor(access, state, onEvent) } }
                "preview" -> {
                    item { Text(uiText(R.string.space_day_72), style = Zapara.typography.caption)
                        if (state.preview != null) ZButton(uiText(R.string.space_day_73), { dispatch(onEvent, GroupSpaceAction.EndPreview) }, ghost = true) }
                    items(state.people, key = { it.id }) { person -> ZButton("${person.name} · ${officialRoleTitle(person.role)}", { dispatch(onEvent, GroupSpaceAction.Preview(person.id, null)) }, enabled = !state.channelBusy, ghost = true) }
                    items(state.desk?.roles.orEmpty(), key = { it.roleId }) { role -> ZButton(role.name, { dispatch(onEvent, GroupSpaceAction.Preview(null, role.roleId)) }, enabled = !state.channelBusy, ghost = true) }
                }
                "audit" -> items(state.audit, key = { it.eventId }) { event -> ZCard(Modifier.fillMaxWidth()) { Text(event.action, style = Zapara.typography.bodyStrong); Text(uiText(R.string.space_day_75, (event.createdAt.atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm"))).toString(), (state.people.firstOrNull { it.id == event.actorId }?.name ?: uiText(R.string.space_day_74)).toString()), style = Zapara.typography.caption) } }
            }
        }
    }
}

@Composable private fun CategoryEditor(category: GroupCategory?, state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    var initialRevision by remember(category?.categoryId) { mutableStateOf(category?.revision ?: 0) }
    var title by rememberSaveable(category?.categoryId) { mutableStateOf(category?.title ?: "") }
    var position by rememberSaveable(category?.categoryId) { mutableStateOf((category?.position ?: state.space?.categories.orEmpty().size).toString()) }
    LaunchedEffect(category?.revision) { if (category != null && category.title == title && category.position.toString() == position) initialRevision = category.revision }
    ZCard(Modifier.fillMaxWidth()) {
        if (category != null && category.revision != initialRevision) ZButton(uiText(R.string.space_day_reload_changes), { title = category.title; position = category.position.toString(); initialRevision = category.revision }, ghost = true)
        Field(if (category == null) uiText(R.string.space_day_76) else uiText(R.string.space_day_77), title, { title = it.take(80) })
        Field(uiText(R.string.space_day_78), position, { position = it })
        ZButton(uiText(R.string.space_day_79), { dispatch(onEvent, GroupSpaceAction.Category(category?.categoryId, title.trim(), position.toInt(), initialRevision)) }, enabled = !state.channelBusy && title.trim().length >= 2 && position.toIntOrNull() != null, busy = state.channelBusy)
        if (category != null) ZButton(uiText(R.string.space_day_80), { dispatch(onEvent, GroupSpaceAction.DeleteCategory(category.categoryId)) }, enabled = !state.channelBusy, ghost = true)
    }
}

@Composable private fun RoleEditor(role: GroupRole, state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
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
    var delete by remember { mutableStateOf(false) }
    LaunchedEffect(role.revision) { if (role.name == name && role.icon == icon && role.position.toString() == position) initialRevision = role.revision }
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
                Field(uiText(R.string.space_day_81), name, { name = it.take(32) }, canSettings)
                Field(uiText(R.string.space_day_82), icon, { icon = it.take(16) }, canSettings)
                Field(uiText(R.string.space_day_83), position, { position = it }, canSettings)
                Text(uiText(R.string.group_roles_level_hint), style = Zapara.typography.caption)
                val maxPosition = if (desk.headman) 10000 else (RoleManagementPolicy.actorPosition(desk, state.people) - 1).coerceAtLeast(0)
                ZButton(uiText(R.string.space_day_84), { dispatch(onEvent, GroupSpaceAction.Role(role.copy(name = name.trim(), icon = icon, position = position.toInt(), revision = initialRevision))) }, enabled = canSettings && !state.channelBusy && role.revision == initialRevision && name.trim().length >= 2 && (position.toIntOrNull()?.let { it in 0..maxPosition } == true))
                ZButton(uiText(R.string.space_day_87), { delete = true; dispatch(onEvent, GroupSpaceAction.RoleImpact(role.roleId)) }, enabled = canSettings && !state.channelBusy, ghost = true)
            }
            "powers" -> {
                settingReason?.let { Text(uiText(it), style = Zapara.typography.caption, color = Zapara.colors.warn) }
                (state.space?.capabilities?.powers ?: desk.capabilities.powers).forEach { power ->
                    val on = desk.powers.any { it.roleId == role.roleId && it.power == power }
                    val allowed = canSettings && (desk.headman || power in desk.mine)
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(on, { dispatch(onEvent, GroupSpaceAction.Power(role.roleId, power, it)) }, enabled = allowed && !state.channelBusy)
                        Text(powerTitle(power), style = Zapara.typography.body)
                    }
                    if (!allowed && !desk.headman && power !in desk.mine) Text(uiText(R.string.group_roles_power_unavailable), style = Zapara.typography.caption)
                }
            }
            "people" -> {
                val grants = desk.grants
                val limit = state.space?.capabilities?.maxRolesPerMember ?: 3
                Text(uiText(R.string.space_day_86, limit.toString()), style = Zapara.typography.section)
                grantReason?.let { Text(uiText(it), style = Zapara.typography.caption, color = Zapara.colors.warn) }
                Field(uiText(R.string.group_roles_person_search), query, { query = it })
                ZChip(uiText(R.string.group_roles_assigned_only), selected = assignedOnly, onClick = { assignedOnly = !assignedOnly })
                val filtered = state.people.filter { person ->
                    (!assignedOnly || grants.any { it.userId == person.id && it.roleId == role.roleId }) &&
                        (query.isBlank() || person.name.contains(query, true) || person.handle.contains(query, true))
                }
                Text(uiText(R.string.group_roles_shown, minOf(filtered.size, 40), filtered.size), style = Zapara.typography.caption)
                filtered.take(40).forEach { person ->
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
        ZButton(uiText(R.string.review_reload_access), { readChoice=null; postChoice=null; dispatch(onEvent,GroupSpaceAction.LoadAccess(access.topicId)) },enabled=!state.channelBusy,ghost=true)
        ZButton(uiText(R.string.review_access_preview), { dispatch(onEvent, GroupSpaceAction.PreviewAccess(access.topicId, rules, revisions.base)) }, enabled = !state.channelBusy && !revisions.conflict, busy = state.channelBusy)
        if (approval != null) {
            Text(uiText(R.string.review_access_effect, approval.response.affectedCount, approval.response.beforeReaders.size, approval.response.afterReaders.size, approval.addedReaders.size, approval.removedReaders.size), style = Zapara.typography.body)
            val ordinary = approval.response.participants.firstOrNull { p -> state.people.firstOrNull { it.id == p.userId }?.role == "member" && state.desk?.grants.orEmpty().none { it.userId == p.userId } }
            if (ordinary == null) Text(uiText(R.string.review_no_ordinary_member), style = Zapara.typography.caption)
            else Text(uiText(R.string.review_ordinary_member, ordinary.afterPermissions.joinToString { power -> uiText(powerResource(power)) }), style = Zapara.typography.caption)
            approval.response.participants.forEach { person ->
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
    val topic = GroupActions.topic(state, state.activeTopicId)
    val permissions = topic?.permissions.orEmpty()
    var completedFilter by rememberSaveable(state.activeTopicId) { mutableStateOf(false) }
    LazyColumn(modifier, contentPadding = PaddingValues(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        item { state.spaceError?.let { Text(it, color = Zapara.colors.bad) }; if (state.channelBusy) LinearProgressIndicator(Modifier.fillMaxWidth()); ZButton(uiText(R.string.space_day_116), { dispatch(onEvent, GroupSpaceAction.ReloadContent) }, enabled = !state.channelBusy, ghost = true) }
        when(state.activeChannelKind) {
            "forms" -> {
                if ("forms" in permissions && state.preview == null) item { FormBuilder(state, onEvent) }
                items(state.forms, key = { it.formId }) { FormCard(it, state, onEvent) }
                if (state.forms.isEmpty()) item { Text(uiText(R.string.space_day_117), style = Zapara.typography.body) }
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
                            DatePickerDialog(nativeContext,{ _,year,month,day -> dispatch(onEvent,GroupSpaceAction.ScheduleDate(LocalDate.of(year,month+1,day))) },date.year,date.monthValue-1,date.dayOfMonth).show()
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
    var open by rememberSaveable(state.activeTopicId) { mutableStateOf(false) }
    var title by rememberSaveable(state.activeTopicId) { mutableStateOf("") }
    var description by rememberSaveable(state.activeTopicId) { mutableStateOf("") }
    var deadline by rememberSaveable(state.activeTopicId) { mutableStateOf("") }
    var anonymous by rememberSaveable(state.activeTopicId) { mutableStateOf(false) }
    var questions by rememberSaveable(state.activeTopicId, stateSaver = questionSaver) { mutableStateOf(listOf(GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList()))) }
    val initialVersion = remember(state.activeTopicId) { state.formCreateVersion }
    LaunchedEffect(state.formCreateVersion) { if (state.formCreateVersion > initialVersion) { open = false; title = ""; description = ""; deadline = ""; questions = listOf(GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList())) } }
    ZCard(Modifier.fillMaxWidth()) {
        ZButton(uiText(R.string.space_day_123), { open = !open }, ghost = true)
        if (open) {
            Field(uiText(R.string.space_day_124), title, { title = it.take(120) }); Field(uiText(R.string.space_day_125), description, { description = it.take(2000) }); Field(uiText(R.string.space_day_126), deadline, { deadline = it })
            Row(verticalAlignment = Alignment.CenterVertically) { Switch(anonymous, { anonymous = it }); Text(uiText(R.string.space_day_127)) }
            questions.forEachIndexed { index, question ->
                Field(uiText(R.string.space_day_128, (index + 1).toString()), question.title, { value -> questions = questions.mapIndexed { i, q -> if (i == index) q.copy(title = value.take(400)) else q } })
                FlowRow { listOf("shortText" to uiText(R.string.space_day_129), "longText" to uiText(R.string.space_day_130), "singleChoice" to uiText(R.string.space_day_131), "multipleChoice" to uiText(R.string.space_day_132)).forEach { (kind, label) -> ZChip(label, selected = question.kind == kind, onClick = { questions = questions.mapIndexed { i, q -> if (i == index) q.copy(kind = kind, options = if (kind in setOf("shortText", "longText")) emptyList() else q.options) else q } }) } }
                Row(verticalAlignment = Alignment.CenterVertically) { Checkbox(question.required, { value -> questions = questions.mapIndexed { i, q -> if (i == index) q.copy(required = value) else q } }); Text(uiText(R.string.space_day_133)) }
                if (question.kind.endsWith("Choice")) Field(uiText(R.string.space_day_134), question.options.joinToString("\n"), { value -> questions = questions.mapIndexed { i, q -> if (i == index) q.copy(options = value.split("\n")) else q } })
                if (questions.size > 1) ZButton(uiText(R.string.space_day_135), { questions = questions.filterIndexed { i, _ -> i != index } }, ghost = true)
            }
            ZButton(uiText(R.string.space_day_136), { questions = questions + GroupFormQuestion(UUID.randomUUID().toString(), "", "shortText", true, emptyList()) }, enabled = questions.size < 30, ghost = true)
            val parsed = runCatching { parseDeadline(deadline) }
            val payload = GroupFormDraft(title, description, parsed.getOrNull(), anonymous, questions).normalized()
            val problem = if (parsed.isFailure) "deadline" else payload.problem(Instant.now())
            if (problem != null) Text(uiText(when(problem) {
                "title" -> R.string.review_form_title; "description" -> R.string.review_form_description; "deadline" -> R.string.review_form_deadline
                "questions" -> R.string.review_form_questions; "question" -> R.string.review_form_question; "options" -> R.string.review_form_options; else -> R.string.review_form_size
            }), style = Zapara.typography.caption, color = Zapara.colors.warn)
            val valid = problem == null
            ZButton(uiText(R.string.space_day_137), { dispatch(onEvent, GroupSpaceAction.CreateForm(title, description, parseDeadline(deadline), anonymous, payload.questions)) }, enabled = valid && !state.channelBusy, busy = state.channelBusy)
            Text(uiText(R.string.space_day_138), style = Zapara.typography.caption)
        }
    }
}

@Composable private fun FormCard(form: GroupForm, state: GroupUiState, onEvent: (GroupEvent) -> Unit) {
    val uiText = rememberUiText()
    var answers by rememberSaveable(form.formId, stateSaver = answerSaver) { mutableStateOf(form.ownResponse?.answers ?: emptyList()) }
    fun set(answer: GroupFormAnswer) { answers = answers.filterNot { it.questionId == answer.questionId } + answer }
    val valid = form.questions.all { q -> !q.required || answers.firstOrNull { it.questionId == q.questionId }?.let { !it.text.isNullOrBlank() || it.choices.isNotEmpty() } == true }
    ZCard(Modifier.fillMaxWidth()) {
        Text(form.title, style = Zapara.typography.section); Text(form.description, style = Zapara.typography.body)
        Text(uiText(R.string.space_day_141, (displayDeadline(form.deadlineAt)).toString(), (if (form.anonymous) uiText(R.string.space_day_139) else uiText(R.string.space_day_140)).toString(), (form.responseCount).toString()), style = Zapara.typography.caption)
        Text(uiText(if (form.anonymous) R.string.review_form_anonymous_audience else R.string.review_form_named_audience), style = Zapara.typography.caption)
        form.questions.forEach { question ->
            val answer = answers.firstOrNull { it.questionId == question.questionId } ?: GroupFormAnswer(question.questionId, null)
            Text(question.title + if (question.required) " *" else "", style = Zapara.typography.bodyStrong)
            if (question.kind in setOf("shortText", "longText")) Field(uiText(R.string.space_day_142), answer.text ?: "", { set(answer.copy(text = it)) }, form.canRespond && !state.channelBusy && state.preview == null)
            else question.options.forEach { option -> Row(verticalAlignment = Alignment.CenterVertically) {
                if (question.kind == "singleChoice") RadioButton(option in answer.choices, { set(answer.copy(choices = listOf(option))) }, enabled = form.canRespond && !state.channelBusy && state.preview == null)
                else Checkbox(option in answer.choices, { on -> set(answer.copy(choices = if (on) answer.choices + option else answer.choices - option)) }, enabled = form.canRespond && !state.channelBusy && state.preview == null)
                Text(option, style = Zapara.typography.body)
            } }
        }
        if (form.canRespond) ZButton(if (form.ownResponse == null) uiText(R.string.space_day_143) else uiText(R.string.space_day_144), { dispatch(onEvent, GroupSpaceAction.SubmitForm(form.formId, answers)) }, enabled = valid && !state.channelBusy && state.preview == null)
        else Text(uiText(R.string.space_day_145), style = Zapara.typography.caption)
        if (form.canViewResponses) ZButton(uiText(R.string.space_day_146), { dispatch(onEvent, GroupSpaceAction.Responses(form.formId)) }, enabled = !state.channelBusy, ghost = true)
        state.responseCounts[form.formId]?.let { total -> Text(uiText(R.string.space_day_response_count, state.responses[form.formId].orEmpty().size, total), style = Zapara.typography.caption) }
        state.responseCursors[form.formId]?.let { cursor -> ZButton(uiText(R.string.space_day_load_more), { dispatch(onEvent, GroupSpaceAction.Responses(form.formId, cursor)) }, enabled = !state.channelBusy, ghost = true) }
        state.responses[form.formId]?.forEach { response ->
            Text(if (form.anonymous) uiText(R.string.space_day_147) else state.people.firstOrNull { it.id == response.respondentId }?.name ?: uiText(R.string.space_day_148), style = Zapara.typography.bodyStrong)
            response.answers.forEach { answer -> Text("${form.questions.firstOrNull { it.questionId == answer.questionId }?.title}: ${answer.text ?: answer.choices.joinToString()}", style = Zapara.typography.body) }
        }
    }
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
