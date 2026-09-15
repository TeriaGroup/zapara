package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.*
import org.junit.Test
import java.io.File

class LessonTypeStyleTest {
    @Test fun known_types_have_distinct_chip_labels_and_inks() {
        val kinds = listOf(
            "лекция" to LessonTypeStyle.Kind.Lecture,
            "лек" to LessonTypeStyle.Kind.Lecture,
            "практика" to LessonTypeStyle.Kind.Practice,
            "пр" to LessonTypeStyle.Kind.Practice,
            "лабораторная" to LessonTypeStyle.Kind.Lab,
            "лаб" to LessonTypeStyle.Kind.Lab,
            "консультация" to LessonTypeStyle.Kind.Consult,
            "конс" to LessonTypeStyle.Kind.Consult,
            "зачёт" to LessonTypeStyle.Kind.Credit,
            "зач" to LessonTypeStyle.Kind.Credit,
            "экзамен" to LessonTypeStyle.Kind.Exam,
            "экз" to LessonTypeStyle.Kind.Exam,
            "курсовая" to LessonTypeStyle.Kind.Course,
            "курс" to LessonTypeStyle.Kind.Course
        )
        kinds.forEach { (raw, kind) -> assertEquals(raw, kind, LessonTypeStyle.kind(raw)) }
        assertNull(LessonTypeStyle.kind(""))
        assertNull(LessonTypeStyle.kind("семинар"))
        val labels = LessonTypeStyle.Kind.entries.map { LessonTypeStyle.chipLabel(it) }
        assertEquals(setOf("Лекция", "Практика", "Лаба", "Консульт.", "Зачёт", "Экзамен", "Курсовая"), labels.toSet())
        assertEquals(labels.size, labels.distinct().size)
        val dark = LessonTypeStyle.Kind.entries.map { LessonTypeStyle.ink(it, dark = true) }
        val light = LessonTypeStyle.Kind.entries.map { LessonTypeStyle.ink(it, dark = false) }
        assertEquals(dark.size, dark.distinct().size)
        assertEquals(light.size, light.distinct().size)
    }

    @Test fun lesson_card_splits_time_from_colored_type_mark() {
        val card = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/schedule/LessonCard.kt").readText()
        assertFalse(card.contains("\${lesson.timeStart} – \${lesson.timeEnd} · \${lesson.type}"))
        assertTrue(card.contains("LessonTypeChip"))
        assertTrue(card.contains("Lesson.Type."))
        val chip = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/schedule/LessonTypeStyle.kt").readText()
        assertTrue(chip.contains("CircleShape"))
        assertFalse(chip.contains("Zapara.space.minTouch"))
    }
}
