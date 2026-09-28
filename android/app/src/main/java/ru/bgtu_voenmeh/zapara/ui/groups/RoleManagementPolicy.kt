package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupDesk
import ru.bgtu_voenmeh.zapara.data.communities.GroupRole
import ru.bgtu_voenmeh.zapara.R

/** UI hints only. The server remains authoritative, including topic access rules. */
internal object RoleManagementPolicy {
    fun actorPosition(desk: GroupDesk, people: List<GroupPersonUi>): Int {
        val self = people.firstOrNull { it.self }?.id ?: return 0
        val held = desk.grants.filter { it.userId == self }.map { it.roleId }.toSet()
        return desk.roles.filter { it.roleId in held }.maxOfOrNull { it.position } ?: 0
    }

    fun roleReason(desk: GroupDesk, people: List<GroupPersonUi>, role: GroupRole, permission: String): Int? {
        if (!desk.headman && permission !in desk.mine) return if (permission == "grants") R.string.group_roles_no_grants else R.string.group_roles_no_manage
        if (desk.headman) return null
        val self = people.firstOrNull { it.self }?.id
        if (desk.grants.any { it.roleId == role.roleId && it.userId == self }) return R.string.group_roles_own
        if (role.position >= actorPosition(desk, people)) return R.string.group_roles_equal
        if (desk.powers.any { it.roleId == role.roleId && it.power !in desk.mine }) return R.string.group_roles_unheld
        return null
    }

    fun personReason(desk: GroupDesk, people: List<GroupPersonUi>, person: GroupPersonUi): Int? {
        if (person.self && !desk.headman) return R.string.group_roles_self
        if (!desk.headman && person.role != "member") return R.string.group_roles_official_protected
        val held = desk.grants.filter { it.userId == person.id }.map { it.roleId }.toSet()
        val targetHeight = desk.roles.filter { it.roleId in held }.maxOfOrNull { it.position } ?: 0
        if (!desk.headman && targetHeight >= actorPosition(desk, people)) return R.string.group_roles_person_equal
        return null
    }
}
