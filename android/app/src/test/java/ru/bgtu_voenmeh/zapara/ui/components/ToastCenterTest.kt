package ru.bgtu_voenmeh.zapara.ui.components

import org.junit.Assert.*
import org.junit.Test

class ToastCenterTest {
    @Test fun same_text_new_event_keeps_the_new_action_and_runs_only_once() {
        val center = ToastCenter()
        var first = 0
        var second = 0
        center.show("Retry", action = ToastAction("Try", { first++ }))
        val oldId = center.items.value.last().id
        center.show("Retry", action = ToastAction("Try", { second++ }))
        val current = center.items.value.last()
        assertNotEquals(oldId, current.id)
        center.invokeAction(oldId)
        center.invokeAction(current.id)
        center.invokeAction(current.id)
        assertEquals(0, first)
        assertEquals(1, second)
    }

    @Test fun repeated_plain_feedback_renews_its_lifetime_identity() {
        val center = ToastCenter()
        center.show("Failed", ToastKind.Bad)
        val first = center.items.value.last().id
        center.show("Failed", ToastKind.Bad)
        assertEquals(1, center.items.value.size)
        assertNotEquals(first, center.items.value.last().id)
        center.dismiss(first)
        assertEquals(1, center.items.value.size)
    }
}
