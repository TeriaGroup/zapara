package ru.bgtu_voenmeh.zapara.data.sync

import java.util.UUID

/** An immutable choice shown to the user; resolution refuses a changed local or server version. */
data class SyncConflict(
    val operation: PrivateSyncOutboxEntry,
    val localValue: SyncValue?,
    val serverRecord: SyncRecord?,
    val parentRecord: SyncRecord?,
    val epoch: UUID?,
    val canKeepLocal: Boolean
) {
    override fun toString() = "SyncConflict { [REDACTED] }"
}
