package ru.bgtu_voenmeh.zapara.ui.widgets

import java.time.LocalDateTime

internal fun stableWidgetGroup(before: String?, after: String?): String? =
    before?.takeIf { it == after }

internal data class WidgetReadGood<T>(val identity: WidgetJobIdentity, val groupId: String,
    val value: T,
    val readAt: LocalDateTime)

/** A failed read may reuse only the last successful snapshot of this exact profile generation. */
internal class WidgetReadCache<T> {
    private var last: WidgetReadGood<T>? = null
    private var activeGroup: String? = null
    fun enterGroup(groupId: String?): Boolean {
        val changed = groupId == null || groupId != activeGroup
        if (changed) last = null
        activeGroup = groupId
        return changed
    }
    fun accept(identity: WidgetJobIdentity, groupId: String, value: T, readAt: LocalDateTime): T {
        if (activeGroup != groupId) enterGroup(groupId)
        last = WidgetReadGood(identity, groupId, value, readAt)
        return value
    }
    fun lastFor(identity: WidgetJobIdentity, groupId: String?): WidgetReadGood<T>? =
        groupId?.takeIf { it == activeGroup }?.let { group ->
            last?.takeIf { WidgetJobs.accept(it.identity, identity) && it.groupId == group }
        }
    fun clear() { last = null; activeGroup = null }
}
