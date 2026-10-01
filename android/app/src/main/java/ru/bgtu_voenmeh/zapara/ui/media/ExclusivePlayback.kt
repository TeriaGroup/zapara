package ru.bgtu_voenmeh.zapara.ui.media

internal class ExclusivePlayback<T : Any>(private val stop: (T) -> Unit) {
    private var active: T? = null

    fun activate(player: T) {
        if (active === player) return
        active?.let(stop)
        active = player
    }

    fun release(player: T) {
        if (active === player) active = null
    }
}
