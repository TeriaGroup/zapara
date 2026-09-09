package ru.bgtu_voenmeh.zapara.ui.friends

object FriendPalette {
    val keys: List<String> = listOf("#F2A33C", "#4CC38A", "#5AA9FF", "#C77DFF", "#FF7A9C")

    fun indexOf(hex: String): Int {
        val n = normalize(hex)
        val known = keys.indexOfFirst { it.equals(n, ignoreCase = true) }
        if (known >= 0) return known
        return n.hashCode().and(0x7fffffff) % keys.size
    }

    fun firstFree(used: List<String>): String {
        val taken = used.map { normalize(it) }.toSet()
        return keys.firstOrNull { normalize(it) !in taken } ?: keys[0]
    }

    private fun normalize(hex: String): String {
        val h = hex.trim()
        return if (h.startsWith("#") && h.length == 9) "#${h.substring(3)}" else h
    }
}

object Strictness {
    val steps = listOf(25, 50, 75, 100)

    fun label(value: Int, copy: ru.bgtu_voenmeh.zapara.ui.UiCopy): String = copy.get(
        when (nearest(value)) {
            25 -> "strict_uni"
            50 -> "strict_building"
            75 -> "strict_floor"
            else -> "strict_room"
        }
    )

    fun nearest(value: Int): Int = steps.minBy { kotlin.math.abs(it - value) }
}
