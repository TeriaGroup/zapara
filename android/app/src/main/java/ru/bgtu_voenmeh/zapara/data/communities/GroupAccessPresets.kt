package ru.bgtu_voenmeh.zapara.data.communities

object GroupAccessPresets {
    val groupOnly = setOf("joins","exclude","roles","grants")
    enum class Mode { All, Selected, Headman, Custom }
    data class Selection(val mode: Mode, val roles: Set<String> = emptySet())
    fun topicRules(rules: List<GroupAccessRule>) = rules.filterNot { it.power in groupOnly }
    fun editorSelection(rules: List<GroupAccessRule>, power: String, explicitMode: String?): Selection {
        val canonical=detect(rules,power)
        // Empty selected-post and headman-only have the same ACL. Preserve the user's
        // picker choice while editing, without changing canonical server semantics.
        return if (power=="post" && explicitMode==Mode.Selected.name && canonical.mode==Mode.Headman)
            Selection(Mode.Selected) else canonical
    }
    fun detect(rules: List<GroupAccessRule>, power: String): Selection {
        val rows = rules.filter { it.power == power && it.state != "inherit" }
        val everyone = rows.firstOrNull { it.roleId == null }
        val roles = rows.filter { it.roleId != null }
        if(power=="post" && rows.isEmpty()) return Selection(Mode.All)
        if(power=="read" && everyone?.state=="allow" && roles.isEmpty()) return Selection(Mode.All)
        if(everyone?.state=="deny" && roles.all { it.state=="allow" }) {
            val allowed=roles.mapNotNull { it.roleId }.toSet()
            return Selection(if(power=="post" && allowed.isEmpty()) Mode.Headman else Mode.Selected,allowed)
        }
        return Selection(Mode.Custom)
    }
    fun apply(rules: List<GroupAccessRule>, power: String, selection: Selection): List<GroupAccessRule> {
        if(selection.mode==Mode.Custom) return rules
        val retained=topicRules(rules).filterNot { it.power==power }
        val rows=when(selection.mode) {
            Mode.All -> if(power=="read") listOf(GroupAccessRule(null,power,"allow")) else emptyList()
            Mode.Selected -> listOf(GroupAccessRule(null,power,"deny")) + selection.roles.sorted().map { GroupAccessRule(it,power,"allow") }
            Mode.Headman -> listOf(GroupAccessRule(null,power,"deny"))
            Mode.Custom -> emptyList()
        }
        return retained+rows
    }
}
