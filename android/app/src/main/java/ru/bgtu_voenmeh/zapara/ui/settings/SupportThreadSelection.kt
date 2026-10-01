package ru.bgtu_voenmeh.zapara.ui.settings

import ru.bgtu_voenmeh.zapara.data.accounts.SupportThread

internal fun selectedSupportThreadId(rows: List<SupportThread>, selectedId: String?,
    preserveNew: Boolean): String? = if (preserveNew && selectedId == null) null
    else selectedId?.takeIf { id -> rows.any { it.id == id } } ?: rows.lastOrNull()?.id

internal fun mergeSupportThread(rows: List<SupportThread>, saved: SupportThread): List<SupportThread> =
    rows.filterNot { it.id == saved.id } + saved

internal fun supportThreadContainsAck(fresh: SupportThread?, acknowledged: SupportThread): Boolean =
    fresh != null && acknowledged.messages.all { it in fresh.messages }

internal fun mergeSupportAcknowledged(rows: List<SupportThread>, acknowledged: Collection<SupportThread>): List<SupportThread> =
    acknowledged.fold(rows) { current, saved ->
        if (supportThreadContainsAck(rows.firstOrNull { it.id == saved.id }, saved)) current
        else mergeSupportThread(current, saved)
    }
