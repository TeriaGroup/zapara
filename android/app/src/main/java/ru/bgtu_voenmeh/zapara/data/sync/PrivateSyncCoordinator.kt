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
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.util.UUID
import java.time.Instant

class PrivateSyncCoordinator(
    private val outbox: RoomSyncOutbox,
    private val work: ProfileWork,
    private val beforeApply: () -> Unit = {},
    private val onApplied: () -> Unit = {}
) {
    private val gate = Mutex()
    private val cycleGate = Mutex()
    private val job = SupervisorJob()
    private val scope = CoroutineScope(job + Dispatchers.IO)
    private var client: PrivateSyncHttpClient? = null
    private var access: (suspend () -> String?)? = null
    private var loop: Job? = null
    private val wake = Channel<Unit>(Channel.CONFLATED)
    private val mutableStatus = MutableStateFlow(CloudSyncStatus())
    val status = mutableStatus.asStateFlow()
    private var issue: PrivateSyncState? = null
    private val retryWindow = SyncRetryWindow()
    private val pendingChanged: () -> Unit = { requestSync() }
    var attached: Boolean = false
        private set

    fun attach(http: PrivateSyncHttpClient, accessToken: suspend () -> String?, background: Boolean = true) {
        if (attached) return
        client = http
        access = accessToken
        attached = true
        mutableStatus.value = mutableStatus.value.copy(attached = true)
        outbox.onPendingChanged = pendingChanged
        if (background) start()
    }

    fun requestSync() {
        if (attached && job.isActive) {
            mutableStatus.update { it.copy(waiting = true) }
            wake.trySend(Unit)
        }
    }

    fun start() {
        if (loop?.isActive == true || !attached) return
        loop = scope.launch {
            var failures = 0
            while (isActive) {
                val cooldown = retryWindow.remainingMillis()
                if (cooldown > 0) delay(cooldown)
                try {
                    sync()
                } catch (e: CancellationException) {
                    throw e
                } catch (_: Exception) { }
                failures = if (status.value.failure == null) 0 else (failures + 1).coerceAtMost(3)
                val waitMs = if (failures == 0) 30_000L else (15_000L shl failures).coerceAtMost(120_000L)
                // Local edits and explicit resume wake a sleeping worker; one burst is one cycle.
                if (withTimeoutOrNull(waitMs) { wake.receive(); true } == true) delay(250)
            }
        }
    }

    /** Establish the full snapshot before sending local edits from a new/expired profile. */
    suspend fun sync() = cycleGate.withLock {
        if (!attached || !outbox.enabled || !job.isActive) return@withLock
        // Keep the profile database open between the individual serialized phases too.
        val cycle = work.enter()
        try {
            if (!cycle.isCurrent) return@withLock
            if (retryWindow.remainingMillis() > 0) {
                publishStatus(running = false)
                return@withLock
            }
            issue = null
            publishStatus(running = true)
            pull()
            if (!cycle.isCurrent) return@withLock
            if (issue == null && outbox.inbox.initialized) pushPending()
            if (cycle.isCurrent && issue == null) pull()
            if (cycle.isCurrent) publishStatus(running = false, succeeded = issue == null && outbox.inbox.initialized)
        } catch (cancelled: CancellationException) {
            if (cycle.isCurrent) publishStatus(running = false)
            throw cancelled
        } catch (error: Exception) {
            issue = PrivateSyncState.Unavailable
            if (cycle.isCurrent) publishStatus(running = false)
            throw error
        } finally {
            mutableStatus.update { it.copy(running = false) }
            cycle.close()
        }
    }

    private fun publishStatus(running: Boolean, succeeded: Boolean = false) {
        val rows = outbox.pending()
        val pending = rows.count { it.status == "pending" }
        val conflicts = rows.count { it.status == "conflict" }
        mutableStatus.update { previous -> CloudSyncStatus(attached, running, pending, conflicts,
            if (succeeded && pending == 0 && conflicts == 0) Instant.now() else previous.lastSuccess,
            issue, waiting = !running && previous.waiting) }
    }

    private fun failed(result: PrivateSyncResult<*>) {
        issue = result.state
        val cooldown = result.retryAfter ?: if (result.state == PrivateSyncState.RateLimited) java.time.Duration.ofSeconds(30) else null
        cooldown?.let(retryWindow::defer)
    }

    suspend fun pushPending() = gate.withLock {
        val ticket = work.enter()
        try {
            if (!ticket.isCurrent) return@withLock
            val http = client ?: return@withLock
            val token = access?.invoke().orEmpty()
            if (token.isEmpty()) { issue = PrivateSyncState.NeedsReauthentication; return@withLock }
            if (!ticket.isCurrent) return@withLock
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
                        beforeApply()
                        outbox.inTransaction {
                            ticket.throwIfStale()
                            outbox.markConflict(current, result.mutationOutcome)
                            ticket.throwIfStale()
                        }
                        onApplied()
                    }
                    PrivateSyncState.ResetRequired -> {
                        failed(result)
                        outbox.abortIfReset(result.state)
                        return@withLock
                    }
                    else -> { failed(result); return@withLock }
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
            beforeApply()
            val result = outbox.inTransaction {
                context.ensureActive(); ticket.throwIfStale()
                val changed = outbox.inbox.resolve(shown, keepLocal)
                context.ensureActive(); ticket.throwIfStale()
                changed
            }
            if (result) { onApplied(); requestSync() }
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
            if (token.isEmpty()) { issue = PrivateSyncState.NeedsReauthentication; return@withLock }
            if (!ticket.isCurrent) return@withLock
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
                        if (begin.state != PrivateSyncState.Success) { failed(begin); return@withLock }
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
                        if (result.state != PrivateSyncState.Success) { failed(result); return@withLock }
                        outbox.inTransaction { checkCurrent(); inbox.stage(result.value!!); checkCurrent() }
                    }
                    if (expired) {
                        if (restarts++ > 0) { issue = PrivateSyncState.ManifestExpired; return@withLock }
                        continue
                    }
                    beforeApply()
                    inbox.publish(checkCurrent)
                    onApplied()
                }
                val result = http.changes(token, outbox.syncEpoch!!, outbox.afterSequence)
                checkCurrent()
                if (result.state == PrivateSyncState.ResetRequired) {
                    outbox.abortExpiredEpoch()
                    if (restarts++ > 0) { issue = PrivateSyncState.ResetRequired; return@withLock }
                    continue
                }
                if (result.state != PrivateSyncState.Success) { failed(result); return@withLock }
                val page = result.value!!
                beforeApply()
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
        if (outbox.onPendingChanged === pendingChanged) outbox.onPendingChanged = null
        wake.close()
        mutableStatus.value = mutableStatus.value.copy(attached = false, running = false, waiting = false)
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
        val value = meta.value ?: run { failed(meta); return null }
        if (meta.state != PrivateSyncState.Success) { failed(meta); return null }
        outbox.setEpoch(value.syncEpoch, value.minAfterSequence)
        return value.syncEpoch
    }
}
