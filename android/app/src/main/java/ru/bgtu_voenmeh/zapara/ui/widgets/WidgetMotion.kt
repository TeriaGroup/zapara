package ru.bgtu_voenmeh.zapara.ui.widgets

data class WidgetMotionPolicy(val enabled: Boolean, val durationMs: Long, val frameCount: Int) {
    fun frames(): List<WidgetMotionFrame> = if (!enabled) listOf(WidgetMotionFrame(0, 1f)) else
        List(frameCount) { index ->
            WidgetMotionFrame(durationMs * index / (frameCount - 1), index / (frameCount - 1f))
        }

    companion object {
        val Disabled = WidgetMotionPolicy(false, 0, 1)

        fun of(appEnabled: Boolean, systemScale: Float, interactive: Boolean): WidgetMotionPolicy =
            if (appEnabled && systemScale > 0f && interactive)
                WidgetMotionPolicy(true, (420f * systemScale).toLong().coerceIn(240, 500), 7)
            else Disabled
    }
}

data class WidgetMotionFrame(val delayMs: Long, val progress: Float)

data class WidgetMotionPose(
    val oldOffsetYDp: Float,
    val oldAlpha: Float,
    val newOffsetYDp: Float,
    val newAlpha: Float
)

enum class WidgetMotionKind { ScheduleShift, HomeworkCompleted, HomeworkShift, Phase, Room, Day }

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
        enabled = policy.enabled
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
