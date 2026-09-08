package ru.bgtu_voenmeh.zapara.data.profiles

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.withTimeoutOrNull

class ProfileWork {
    private val gate = Any()
    private var accepting = true
    private var outstanding = 0
    private var generation = 0L
    private var idle = completed()
    private var closed = false

    val currentGeneration: Long get() = synchronized(gate) { generation }

    fun enter(): Ticket {
        synchronized(gate) {
            val admitted = accepting && !closed
            if (admitted && outstanding++ == 0) idle = CompletableDeferred()
            return Ticket(this, generation, admitted)
        }
    }

    fun stopAccepting() {
        synchronized(gate) {
            if (!accepting) return
            accepting = false
            generation++
        }
    }

    fun resume() {
        synchronized(gate) {
            if (accepting || closed) return
            generation++
            accepting = true
        }
    }

    suspend fun whenIdle(timeoutMs: Long): Boolean {
        val wait = synchronized(gate) { idle }
        return withTimeoutOrNull(timeoutMs) { wait.await() } != null
    }

    fun retire() {
        synchronized(gate) {
            if (accepting || outstanding != 0) throw IllegalStateException("Профиль ещё выполняет работы.")
            closed = true
        }
    }

    internal fun current(ticket: Ticket): Boolean = synchronized(gate) {
        ticket.admitted && !ticket.released && accepting && ticket.generation == generation
    }

    internal fun release(ticket: Ticket) {
        synchronized(gate) {
            if (ticket.released) return
            ticket.released = true
            if (ticket.admitted && --outstanding == 0) idle.complete(Unit)
        }
    }

    class Ticket internal constructor(
        private val owner: ProfileWork,
        internal val generation: Long,
        internal val admitted: Boolean
    ) : AutoCloseable {
        internal var released = false
        val token: Job? = null
        val isCurrent: Boolean get() = owner.current(this)
        fun throwIfStale() {
            if (!isCurrent) throw CancellationException("stale profile")
        }
        override fun close() = owner.release(this)
    }

    companion object {
        private fun completed(): CompletableDeferred<Unit> {
            val d = CompletableDeferred<Unit>()
            d.complete(Unit)
            return d
        }
    }
}

class ProfileGraph(
    val descriptor: ProfileDescriptor,
    val store: ru.bgtu_voenmeh.zapara.data.api.TimetableStore,
    val work: ProfileWork,
    private val onClose: () -> Unit = {}
) {
    var closed: Boolean = false
        private set

    fun close() {
        if (closed) return
        work.stopAccepting()
        runCatching { work.retire() }
        closed = true
        onClose()
    }
}
