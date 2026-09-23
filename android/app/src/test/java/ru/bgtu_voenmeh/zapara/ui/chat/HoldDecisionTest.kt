package ru.bgtu_voenmeh.zapara.ui.chat

import org.junit.Assert.assertEquals
import org.junit.Test

class HoldDecisionTest {
    @Test
    fun holdOpensActionsAndTapDoesNot() {
        assertEquals(emptyList<String>(), HoldDecision.actions("text", true, false, false))
        assertEquals(emptyList<String>(), HoldDecision.actions("image", true, false, false))
        assertEquals(listOf("reply", "reaction", "edit", "delete"), HoldDecision.actions("text", true, false, true))
        assertEquals(listOf("reply", "reaction", "delete"), HoldDecision.actions("video", true, false, true))
        assertEquals(listOf("reply", "reaction"), HoldDecision.actions("file", false, false, true))
        assertEquals(emptyList<String>(), HoldDecision.actions("text", true, true, true))
        assertEquals(listOf("reply", "reaction", "delete"), HoldDecision.actions("image", true, false, true))
        val called = mutableListOf<String>()
        HoldDecision.perform("reply", { called += "reply" }, { called += "reaction" }, { called += "edit" }, { called += "delete" })
        HoldDecision.perform("reaction", { called += "reply" }, { called += "reaction" }, { called += "edit" }, { called += "delete" })
        HoldDecision.perform("edit", { called += "reply" }, { called += "reaction" }, { called += "edit" }, { called += "delete" })
        HoldDecision.perform("delete", { called += "reply" }, { called += "reaction" }, { called += "edit" }, { called += "delete" })
        assertEquals(listOf("reply", "reaction", "edit", "delete"), called)
    }
}
