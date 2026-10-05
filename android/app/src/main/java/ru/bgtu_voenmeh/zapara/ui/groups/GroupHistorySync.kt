package ru.bgtu_voenmeh.zapara.ui.groups

import java.time.Instant

/** Amend loaded rows, retain new history, and never undo a mutation acknowledged during the GET. */
internal fun mergeGroupHistory(
    current: List<GroupMessageUi>,
    atRequest: List<GroupMessageUi>,
    incoming: List<GroupMessageUi>,
    recent: List<GroupMessageUi>
): List<GroupMessageUi> {
    val before = atRequest.associateBy { it.id }
    val fresh = (incoming + recent).associateBy { it.id }
    val result = linkedMapOf<String, GroupMessageUi>()
    for (row in current) result[row.id] = if (before[row.id] == row) fresh[row.id] ?: row else row
    for (row in incoming) if (row.id !in result) result[row.id] = fresh[row.id] ?: row
    return result.values.sortedBy { it.createdAt ?: Instant.EPOCH }
}
