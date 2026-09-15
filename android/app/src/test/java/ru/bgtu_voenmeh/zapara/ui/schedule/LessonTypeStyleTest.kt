package ru.bgtu_voenmeh.zapara.ui.schedule

import org.junit.Assert.*
import org.junit.Test
import java.io.File
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.LessonTypeKind

class LessonTypeStyleTest {
    @Test fun known_types_have_distinct_chip_labels_and_inks() {
        val kinds = listOf(
            "лекция" to LessonTypeKind.Lecture,
            "лек" to LessonTypeKind.Lecture,
            "практика" to LessonTypeKind.Practice,
            "пр" to LessonTypeKind.Practice,
            "лабораторная" to LessonTypeKind.Lab,
            "лаб" to LessonTypeKind.Lab,
            "консультация" to LessonTypeKind.Consult,
            "конс" to LessonTypeKind.Consult,
            "зачёт" to LessonTypeKind.Credit,
            "зач" to LessonTypeKind.Credit,
            "экзамен" to LessonTypeKind.Exam,
            "экз" to LessonTypeKind.Exam,
            "курсовая" to LessonTypeKind.Course,
            "курс" to LessonTypeKind.Course
        )
        kinds.forEach { (raw, kind) -> assertEquals(raw, kind, LessonTypeStyle.kind(raw)) }
        assertNull(LessonTypeStyle.kind(""))
        assertNull(LessonTypeStyle.kind("семинар"))
        val labels = LessonTypeKind.entries.map { LessonTypeStyle.labelRes(it) }
        assertEquals(
            setOf(
                R.string.type_chip_lecture, R.string.type_chip_practice, R.string.type_chip_lab,
                R.string.type_chip_consult, R.string.type_chip_credit, R.string.type_chip_exam, R.string.type_chip_course
            ),
            labels.toSet()
        )
        assertEquals(labels.size, labels.distinct().size)
        val dark = LessonTypeKind.entries.map { LessonTypeStyle.ink(it, dark = true) }
        val light = LessonTypeKind.entries.map { LessonTypeStyle.ink(it, dark = false) }
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
