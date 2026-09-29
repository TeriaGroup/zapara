package ru.bgtu_voenmeh.zapara.data.sync

import java.time.Instant

data class CloudSyncStatus(
    val attached: Boolean = false,
    val running: Boolean = false,
    val pending: Int = 0,
    val conflicts: Int = 0,
    val lastSuccess: Instant? = null,
    val failure: PrivateSyncState? = null,
    val waiting: Boolean = false
) {
    val upToDate: Boolean get() = attached && !running && !waiting && pending == 0 && conflicts == 0 && failure == null && lastSuccess != null
}
