package ru.bgtu_voenmeh.zapara.ui.widgets

data class WidgetMotionPolicy(val enabled: Boolean, val durationMs: Long, val frameCount: Int) {
    val isPlayable: Boolean
        get() = enabled && durationMs in 1L..500L && frameCount in 2..9 && durationMs >= frameCount - 1L

    fun frames(): List<WidgetMotionFrame> = if (!isPlayable) listOf(WidgetMotionFrame(0, 1f)) else
        List(frameCount) { index ->
            WidgetMotionFrame(durationMs * index / (frameCount - 1), index / (frameCount - 1f))
        }

    companion object {
        val Disabled = WidgetMotionPolicy(false, 0, 1)

        fun of(appEnabled: Boolean, systemScale: Float, interactive: Boolean): WidgetMotionPolicy =
            if (appEnabled && systemScale.isFinite() && systemScale > 0f && interactive)
                WidgetMotionPolicy(true, (420.0 * systemScale.toDouble()).coerceIn(240.0, 500.0).toLong(), 7)
            else Disabled
    }
}

/** Pure monotonic-clock calculations; callers post only the next frame, never a backlog. */
internal object WidgetMotionTiming {
    /** Do not reveal the old bitmap after the final face has already been visible for a full slot. */
    fun mayStartAt(elapsedMs: Long, durationMs: Long, frameCount: Int): Boolean {
        val firstSlot = nextOffsetAfter(0L, durationMs, frameCount) ?: return false
        return elapsedMs < firstSlot
    }

    fun progressAt(nowMs: Long, startedMs: Long, durationMs: Long): Float {
        if (durationMs <= 0L) return 1f
        if (nowMs <= startedMs) return 0f
        return ((nowMs - startedMs).toDouble() / durationMs).coerceIn(0.0, 1.0).toFloat()
    }

    fun nextOffsetAfter(elapsedMs: Long, durationMs: Long, frameCount: Int): Long? {
        if (durationMs !in 1L..500L || frameCount !in 2..9 || elapsedMs >= durationMs) return null
        val steps = frameCount - 1
        for (index in 1..steps) {
            val offset = durationMs / steps * index + durationMs % steps * index / steps
            if (offset > elapsedMs) return offset
        }
        return null
    }
}

data class WidgetMotionFrame(val delayMs: Long, val progress: Float)

data class WidgetMotionPose(
    val oldOffsetYDp: Float,
    val oldAlpha: Float,
    val newOffsetYDp: Float,
    val newAlpha: Float
)

enum class WidgetMotionKind { ScheduleShift, ScheduleUpdated, HomeworkCompleted, HomeworkShift, HomeworkUpdated, Phase, Room, Day }

/** Revision is reserved before IO starts. Preference writes block all reads until completion. */
internal class WidgetMotionPolicies {
    private var sequence = 0L
    private var acceptedRevision = 0L
    private var identity: WidgetJobIdentity? = null
    private var enabled = true
    private val pendingWrites = mutableSetOf<Long>()

    @Synchronized fun beginObservation(): Long = ++sequence

    @Synchronized fun update(expected: WidgetJobIdentity, policy: WidgetMotionPolicy, revision: Long): Boolean {
        if (revision <= acceptedRevision) return false
        select(expected)
        if (pendingWrites.isNotEmpty()) return false
        acceptedRevision = revision
        enabled = policy.isPlayable
        return true
    }

    @Synchronized fun beginChange(expected: WidgetJobIdentity): Long {
        select(expected)
        val revision = ++sequence
        acceptedRevision = revision
        enabled = false
        pendingWrites += revision
        return revision
    }

    @Synchronized fun finishChange(expected: WidgetJobIdentity, revision: Long) {
        if (identity != expected || !pendingWrites.remove(revision)) return
        // Invalidate reads started before OR during the write, including failed/cancelled saves.
        acceptedRevision = ++sequence
        enabled = false
    }

    @Synchronized fun allows(expected: WidgetJobIdentity): Boolean = identity != expected || enabled

    private fun select(expected: WidgetJobIdentity) {
        if (identity != expected) {
            identity = expected
            pendingWrites.clear()
        }
    }
}

/** Invert x before evaluating y: the project curve is cubic-bezier(0.2, 0.8, 0.2, 1). */
fun widgetMotionEase(progress: Float): Float {
    if (progress <= 0f) return 0f
    if (progress >= 1f || progress.isNaN()) return 1f
    fun cubic(t: Float, first: Float, second: Float): Float {
        val remaining = 1f - t
        return 3f * remaining * remaining * t * first + 3f * remaining * t * t * second + t * t * t
    }
    var lower = 0f
    var upper = 1f
    repeat(20) {
        val middle = (lower + upper) / 2f
        if (cubic(middle, 0.2f, 0.2f) < progress) lower = middle else upper = middle
    }
    return cubic((lower + upper) / 2f, 0.8f, 1f)
}

fun widgetMotionPose(progress: Float): WidgetMotionPose {
    val eased = widgetMotionEase(progress)
    return WidgetMotionPose(if (eased == 0f) 0f else -12f * eased, 1f - eased, 12f * (1f - eased), eased)
}

/** Main-thread owner; a process-wide sequence prevents token reuse after pruning an ID. */
class WidgetMotionTokens {
    private var generation = 0L
    private val current = mutableMapOf<Int, Long>()

    fun next(id: Int): Long = (++generation).also { current[id] = it }
    fun isCurrent(id: Int, token: Long): Boolean = current[id] == token
    fun mayDraw(id: Int, token: Long, expected: WidgetJobIdentity, current: WidgetJobIdentity): Boolean =
        isCurrent(id, token) && WidgetJobs.accept(expected, current)

    fun retainIds(ids: Set<Int>) { current.keys.retainAll(ids) }
}

/** Deliberately process-only: animation never persists prior text or bitmaps. */
internal class WidgetMotionHistory<T> {
    private val faces = mutableMapOf<Int, Pair<WidgetJobIdentity, T>>()
    fun previous(id: Int, identity: WidgetJobIdentity): T? = faces[id]?.takeIf { it.first == identity }?.second
    fun remember(id: Int, identity: WidgetJobIdentity, face: T) { faces[id] = identity to face }
    fun forget(id: Int) { faces.remove(id) }
    fun retainIds(ids: Set<Int>) { faces.keys.retainAll(ids) }
    fun clear() { faces.clear() }
}
