package ru.bgtu_voenmeh.zapara.ui.communities

import ru.bgtu_voenmeh.zapara.R

/** #109 / AN-22: каталог сообществ — один слот статуса, единственная кнопка «Вступить», счётчики от 10 пунктов. */
object CatalogRules {
    const val COUNTERS_FROM = 10

    fun showCounters(total: Int): Boolean = total >= COUNTERS_FROM

    /** Строка ресурса статуса для единственного чипа строки; null — статуса нет (тогда может быть «Вступить»). */
    fun status(role: String?, joinStatus: String?, joining: Boolean): Int? = when {
        role == "headman" -> R.string.group_role_headman
        role == "curator" -> R.string.group_role_curator
        role != null -> R.string.group_role_member
        joining -> R.string.ux30_community_joining
        joinStatus == "pending" -> R.string.community_pending
        joinStatus == "accepted" -> R.string.ux30_community_join_accepted
        else -> null
    }
}
