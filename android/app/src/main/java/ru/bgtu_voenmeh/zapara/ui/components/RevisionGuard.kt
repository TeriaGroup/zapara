package ru.bgtu_voenmeh.zapara.ui.components

import androidx.compose.runtime.saveable.listSaver

/** A background update may advance latest, never the write's expected revision. */
data class RevisionGuard(val base: Long, val latest: Long = base) {
    val conflict: Boolean get() = base != latest
    fun observed(revision: Long) = copy(latest = maxOf(latest,revision))
    fun confirmed(revision: Long) = RevisionGuard(revision)
    fun acknowledgedOwn(revision: Long) = copy(base=revision,latest=maxOf(latest,revision))
    fun reload() = RevisionGuard(latest)
    companion object {
        val Saver = listSaver<RevisionGuard, Long>(save = { listOf(it.base, it.latest) }, restore = { RevisionGuard(it[0], it[1]) })
    }
}
