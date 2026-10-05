package ru.bgtu_voenmeh.zapara.data.communities

data class GroupRole(val roleId: String, val name: String, val position: Int = 0, val icon: String = "", val revision: Long = 0)
data class GroupGrant(val roleId: String, val userId: String)
data class GroupPower(val roleId: String, val power: String)

data class GroupDesk(
    val headman: Boolean,
    val roles: List<GroupRole>,
    val grants: List<GroupGrant>,
    val powers: List<GroupPower>,
    val mine: List<String>,
    val capabilities: GroupCapabilities = GroupCapabilities()
)
