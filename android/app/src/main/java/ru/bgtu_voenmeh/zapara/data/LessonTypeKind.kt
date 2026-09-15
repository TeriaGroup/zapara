package ru.bgtu_voenmeh.zapara.data

import java.util.Locale

enum class LessonTypeKind { Lecture, Practice, Lab, Consult, Credit, Exam, Course }

object LessonTypeKinds {
    fun of(type: String): LessonTypeKind? = when (type.trim().lowercase(Locale("ru"))) {
        "лек", "лекция" -> LessonTypeKind.Lecture
        "пр", "практика" -> LessonTypeKind.Practice
        "лаб", "лабораторная", "лабораторная работа" -> LessonTypeKind.Lab
        "конс", "консультация" -> LessonTypeKind.Consult
        "зач", "зачёт", "зачет" -> LessonTypeKind.Credit
        "экз", "экзамен" -> LessonTypeKind.Exam
        "курс", "курсовая" -> LessonTypeKind.Course
        else -> null
    }
}
