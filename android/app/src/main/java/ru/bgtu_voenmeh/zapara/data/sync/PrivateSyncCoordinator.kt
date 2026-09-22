package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.util.UUID

class PrivateSyncCoordinator(
    private val outbox: RoomSyncOutbox,
    private val work: ProfileWork,
    private val onApplied: () -> Unit = {}
) {
    private val gate = Mutex()
    private val job = SupervisorJob()
    private val scope = CoroutineScope(job + Dispatchers.IO)
    private var client: PrivateSyncHttpClient? = null
    private var access: (suspend () -> String?)? = null
    private var loop: Job? = null
    var attached: Boolean = false
        private set

    fun attach(http: PrivateSyncHttpClient, accessToken: suspend () -> String?, background: Boolean = true) {
        if (attached) return
        client = http
        access = accessToken
        attached = true
        if (background) start()
    }

    fun start() {
        if (loop != null) return
        loop = scope.launch {
            while (isActive) {
                try {
                    sync()
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaSync", "private sync", e)
                }
                delay(30_000)
            }
        }
    }

    /** Establish the full snapshot before sending local edits from a new/expired profile. */
    suspend fun sync() {
        // Keep the profile database open between the individual serialized phases too.
        val cycle = work.enter()
        try {
            if (!cycle.isCurrent) return
            pull()
            if (!cycle.isCurrent) return
            if (outbox.inbox.initialized) pushPending()
            if (cycle.isCurrent) pull()
        } finally { cycle.close() }
    }

    suspend fun pushPending() = gate.withLock {
        val ticket = work.enter()
        try {
            if (!ticket.isCurrent) return@withLock
            val http = client ?: return@withLock
            val token = access?.invoke().orEmpty()
            if (token.isEmpty() || !ticket.isCurrent) return@withLock
            val pending = outbox.pending().filter { it.status == "pending" }
                .sortedWith(compareBy<PrivateSyncOutboxEntry> { if (it.syncEpoch != null) 0 else 1 }
                    .thenBy { if (it.entityType == "completion" && it.action == "upsert") 1 else 0 }
                    .thenBy { it.createdAtUtc })
            if (pending.isEmpty()) return@withLock
            val epoch = ensureEpoch(http, token, ticket) ?: return@withLock
            for (row in pending) {
                currentCoroutineContext().ensureActive()
                if (!ticket.isCurrent) return@withLock
                val current = outbox.find(row.opId) ?: continue
                if (current.status != "pending") continue
                if (current.action == "delete" && current.expectedRevision == 0L) continue
                val mutation = outbox.buildMutation(current, epoch)
                val result = http.mutate(token, mutation)
                currentCoroutineContext().ensureActive()
                if (!ticket.isCurrent) return@withLock
                when (result.state) {
                    PrivateSyncState.Success -> {
                        val record = result.value?.serverRecord ?: result.mutationOutcome?.serverRecord
                        if (record != null) outbox.inTransaction {
                            ticket.throwIfStale()
                            outbox.applyAck(current, record)
                            ticket.throwIfStale()
                        }
                    }
                    PrivateSyncState.Conflict -> {
                        outbox.inTransaction {
                            ticket.throwIfStale()
                            outbox.markConflict(current, result.mutationOutcome)
                            ticket.throwIfStale()
                        }
                        onApplied()
                    }
                    PrivateSyncState.ResetRequired -> {
                        outbox.abortIfReset(result.state)
                        return@withLock
                    }
                    else -> return@withLock
                }
            }
        } catch (e: CancellationException) {
            currentCoroutineContext().ensureActive()
            if (ticket.isCurrent) throw e
        } finally {
            ticket.close()
        }
    }

    suspend fun resolve(shown: SyncConflict, keepLocal: Boolean): Boolean = gate.withLock {
        val ticket = work.enter()
        try {
            if (!ticket.isCurrent) return@withLock false
            val context = currentCoroutineContext()
            val result = outbox.inTransaction {
                context.ensureActive(); ticket.throwIfStale()
                val changed = outbox.inbox.resolve(shown, keepLocal)
                context.ensureActive(); ticket.throwIfStale()
                changed
            }
            if (result) onApplied()
            result
        } finally { ticket.close() }
    }

    suspend fun pull() = gate.withLock {
        if (!outbox.enabled) return@withLock
        val ticket = work.enter()
        try {
            if (!ticket.isCurrent) return@withLock
            val http = client ?: return@withLock
            val token = access?.invoke().orEmpty()
            if (token.isEmpty() || !ticket.isCurrent) return@withLock
            val context = currentCoroutineContext()
            val checkCurrent = { context.ensureActive(); ticket.throwIfStale() }
            val inbox = outbox.inbox
            // At most one expired manifest restart per cycle; unavailable servers never spin.
            var restarts = 0
            while (true) {
                checkCurrent()
                if (!inbox.initialized || inbox.manifest != null) {
                    if (inbox.manifest == null) {
                        val begin = http.beginResync(token)
                        checkCurrent()
                        if (begin.state != PrivateSyncState.Success) return@withLock
                        outbox.inTransaction { checkCurrent(); inbox.begin(begin.value!!); checkCurrent() }
                    }
                    val manifest = inbox.manifest!!
                    var expired = false
                    while (inbox.afterOrdinal < manifest.itemCount) {
                        val result = http.readResyncPage(token, manifest, inbox.afterOrdinal)
                        checkCurrent()
                        if (result.state == PrivateSyncState.ManifestExpired) {
                            inbox.discardManifest()
                            expired = true
                            break
                        }
                        if (result.state != PrivateSyncState.Success) return@withLock
                        outbox.inTransaction { checkCurrent(); inbox.stage(result.value!!); checkCurrent() }
                    }
                    if (expired) {
                        if (restarts++ > 0) return@withLock
                        continue
                    }
                    inbox.publish(checkCurrent)
                    onApplied()
                }
                val result = http.changes(token, outbox.syncEpoch!!, outbox.afterSequence)
                checkCurrent()
                if (result.state == PrivateSyncState.ResetRequired) {
                    outbox.abortExpiredEpoch()
                    if (restarts++ > 0) return@withLock
                    continue
                }
                if (result.state != PrivateSyncState.Success) return@withLock
                val page = result.value!!
                if (inbox.applyChanges(page, checkCurrent)) onApplied()
                if (!page.hasMore) return@withLock
            }
        } catch (e: CancellationException) {
            // A retired profile is an ignored response; cancellation of the calling job
            // must still propagate rather than allowing more requests after logout.
            currentCoroutineContext().ensureActive()
            if (ticket.isCurrent) throw e
        } finally { ticket.close() }
    }

    fun close() {
        loop?.cancel()
        loop = null
        job.cancel()
        attached = false
        client = null
        access = null
    }

    private suspend fun ensureEpoch(
        http: PrivateSyncHttpClient,
        token: String,
        ticket: ProfileWork.Ticket
    ): UUID? {
        outbox.syncEpoch?.let { return it }
        val meta = http.metadata(token)
        if (!ticket.isCurrent) return null
        val value = meta.value ?: return null
        if (meta.state != PrivateSyncState.Success) return null
        outbox.setEpoch(value.syncEpoch, value.minAfterSequence)
        return value.syncEpoch
    }
}
