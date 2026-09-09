package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.util.UUID

class PrivateSyncCoordinator(
    private val outbox: RoomSyncOutbox,
    private val work: ProfileWork
) {
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
                    pushPending()
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaSync", "private sync", e)
                }
                delay(30_000)
            }
        }
    }

    suspend fun pushPending() {
        val ticket = work.enter()
        try {
            if (!ticket.isCurrent) return
            val http = client ?: return
            val token = access?.invoke().orEmpty()
            if (token.isEmpty()) return
            val pending = outbox.pending().filter { it.status == "pending" }
            if (pending.isEmpty()) return
            val epoch = ensureEpoch(http, token, ticket) ?: return
            for (row in pending) {
                if (!ticket.isCurrent) return
                val current = outbox.find(row.opId) ?: continue
                val mutation = outbox.buildMutation(current, epoch)
                val result = http.mutate(token, mutation)
                if (!ticket.isCurrent) return
                when (result.state) {
                    PrivateSyncState.Success -> {
                        val record = result.value?.serverRecord ?: result.mutationOutcome?.serverRecord
                        if (record != null) outbox.applyAck(outbox.find(row.opId) ?: current, record)
                    }
                    PrivateSyncState.ResetRequired -> {
                        outbox.abortIfReset(result.state)
                        return
                    }
                    else -> return
                }
            }
        } finally {
            ticket.close()
        }
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
