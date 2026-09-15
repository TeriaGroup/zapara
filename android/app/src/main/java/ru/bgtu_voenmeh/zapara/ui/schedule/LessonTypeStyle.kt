package ru.bgtu_voenmeh.zapara.ui.schedule

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.util.Locale

object LessonTypeStyle {
    enum class Kind { Lecture, Practice, Lab, Consult, Credit, Exam, Course }

    fun kind(type: String): Kind? = when (type.trim().lowercase(Locale("ru"))) {
        "лек", "лекция" -> Kind.Lecture
        "пр", "практика" -> Kind.Practice
        "лаб", "лабораторная", "лабораторная работа" -> Kind.Lab
        "конс", "консультация" -> Kind.Consult
        "зач", "зачёт", "зачет" -> Kind.Credit
        "экз", "экзамен" -> Kind.Exam
        "курс", "курсовая" -> Kind.Course
        else -> null
    }

    fun chipLabel(kind: Kind): String = when (kind) {
        Kind.Lecture -> "Лекция"
        Kind.Practice -> "Практика"
        Kind.Lab -> "Лаба"
        Kind.Consult -> "Консульт."
        Kind.Credit -> "Зачёт"
        Kind.Exam -> "Экзамен"
        Kind.Course -> "Курсовая"
    }

    fun ink(kind: Kind, dark: Boolean): Color = when (kind) {
        Kind.Lecture -> if (dark) Color(0xFF5AA9FF) else Color(0xFF2B7FD9)
        Kind.Practice -> if (dark) Color(0xFF4CC38A) else Color(0xFF2FA36B)
        Kind.Lab -> if (dark) Color(0xFFF2A33C) else Color(0xFFD9861B)
        Kind.Consult -> if (dark) Color(0xFFC77DFF) else Color(0xFF9B51E0)
        Kind.Credit -> if (dark) Color(0xFF5EEAD4) else Color(0xFF0F766E)
        Kind.Exam -> if (dark) Color(0xFFEF5B6B) else Color(0xFFD7404F)
        Kind.Course -> if (dark) Color(0xFFFF7A9C) else Color(0xFFE0527A)
    }

    fun wash(kind: Kind, dark: Boolean): Color = ink(kind, dark).copy(alpha = 0.18f)
}

@Composable
fun LessonTypeChip(type: String, tag: String, modifier: Modifier = Modifier) {
    if (type.isBlank()) return
    val kind = LessonTypeStyle.kind(type)
    val dark = Zapara.colors.isDark
    val ink = kind?.let { LessonTypeStyle.ink(it, dark) } ?: Zapara.colors.text2
    val wash = kind?.let { LessonTypeStyle.wash(it, dark) } ?: Zapara.colors.chip
    val label = kind?.let { LessonTypeStyle.chipLabel(it) } ?: type
    Row(
        modifier
            .testTag(tag)
            .clip(RoundedCornerShape(Zapara.radii.pill))
            .background(wash)
            .padding(horizontal = Zapara.space.s, vertical = Zapara.space.xs),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(ink))
        Text(label, style = Zapara.typography.caption, color = ink)
    }
}
