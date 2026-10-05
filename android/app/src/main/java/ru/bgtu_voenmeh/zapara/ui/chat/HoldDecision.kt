package ru.bgtu_voenmeh.zapara.ui.chat

object HoldDecision {
    fun actions(kind: String, mine: Boolean, deleted: Boolean, held: Boolean, canModerate: Boolean = false): List<String> {
        if (!held || deleted) return emptyList()
        val actions = mutableListOf("reply", "reaction")
        if (mine && kind == "text") actions += "edit"
        if (mine || canModerate) actions += "delete"
        return actions
    }

    fun perform(action: String, reply: () -> Unit, reaction: () -> Unit, edit: () -> Unit, delete: () -> Unit) {
        when (action) {
            "reply" -> reply()
            "reaction" -> reaction()
            "edit" -> edit()
            "delete" -> delete()
        }
    }
}
