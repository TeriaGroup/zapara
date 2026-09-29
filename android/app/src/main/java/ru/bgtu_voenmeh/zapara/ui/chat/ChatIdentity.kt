package ru.bgtu_voenmeh.zapara.ui.chat

import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

fun avatarInitials(name: String): String {
    val words = name.trim().split(Regex("\\s+")).filter { word -> word.codePoints().anyMatch(Character::isLetterOrDigit) }
    if (words.isEmpty()) return "?"
    fun letters(word: String) = word.codePoints().filter(Character::isLetterOrDigit).toArray()
    val points = if (words.size == 1) letters(words.first()).take(2)
        else listOf(letters(words.first()).first(), letters(words.last()).first())
    return buildString { points.forEach { appendCodePoint(it) } }.uppercase(Locale.ROOT)
}

fun chatDayLabel(date: LocalDate, today: LocalDate, todayLabel: String, yesterdayLabel: String): String = when (date) {
    today -> todayLabel
    today.minusDays(1) -> yesterdayLabel
    else -> date.format(DateTimeFormatter.ofPattern(if (date.year == today.year) "d MMMM" else "d MMMM yyyy", Locale.forLanguageTag("ru")))
}

fun chatListTime(at: Instant, today: LocalDate, zone: ZoneId, yesterdayLabel: String): String {
    val local = at.atZone(zone)
    return if (local.toLocalDate() == today) local.format(DateTimeFormatter.ofPattern("HH:mm"))
        else if (local.toLocalDate() == today.minusDays(1)) yesterdayLabel
        else local.format(DateTimeFormatter.ofPattern(if (local.year == today.year) "dd.MM" else "dd.MM.yy"))
}

fun samePersonalCluster(previous: SocialMessage?, current: SocialMessage, zone: ZoneId): Boolean =
    previous != null && previous.senderId == current.senderId && !previous.deleted && !current.deleted &&
        current.replyTo == null && previous.createdAt.atZone(zone).toLocalDate() == current.createdAt.atZone(zone).toLocalDate() &&
        Duration.between(previous.createdAt, current.createdAt).seconds in 0..300
