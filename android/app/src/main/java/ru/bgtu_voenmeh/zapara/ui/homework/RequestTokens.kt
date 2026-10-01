package ru.bgtu_voenmeh.zapara.ui.homework

/** An old A response cannot finish a newer operation on the same key after A→B→A. */
internal class RequestTokens<K> {
    private val active = HashMap<K, Long>()
    private var next = 0L
    fun begin(key: K): Long? {
        if (key in active) return null
        val token = ++next
        active[key] = token
        return token
    }
    fun current(key: K, token: Long): Boolean = active[key] == token
    fun finish(key: K, token: Long): Boolean {
        if (!current(key, token)) return false
        active.remove(key)
        return true
    }
    fun clear() { active.clear() }
}
