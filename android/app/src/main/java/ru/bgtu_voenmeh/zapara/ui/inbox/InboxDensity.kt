package ru.bgtu_voenmeh.zapara.ui.inbox

/** #108 / AN-13: правила плотности списка чатов. */
object InboxDensity {
    const val FILTERS_FROM = 10

    fun showFilters(chats: Int, activeFilters: Int): Boolean = chats >= FILTERS_FROM || activeFilters > 0
}
