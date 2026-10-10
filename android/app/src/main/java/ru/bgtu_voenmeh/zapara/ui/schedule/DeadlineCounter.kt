package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.R

/** #117 follow-up: счётчик сроков — «Ближайшие сроки · N · выполнено K», часть про выполненные только при K > 0. */
object DeadlineCounter {
    fun res(done: Int): Int = if (done > 0) R.string.deadlines_title_count_done else R.string.deadlines_title_count
}
