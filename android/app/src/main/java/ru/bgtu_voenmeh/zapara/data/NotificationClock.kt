package ru.bgtu_voenmeh.zapara.data

import java.time.LocalDate

/** Evening alarm previews tomorrow. Homework status stays on the wall-clock day. */
data class NotificationClock(val content: LocalDate, val homework: LocalDate)

fun notificationClock(today: LocalDate, firedTime: String?, eveningTime: String?): NotificationClock {
    val evening = !firedTime.isNullOrBlank() && firedTime == eveningTime
    return NotificationClock(content = if (evening) today.plusDays(1) else today, homework = today)
}
