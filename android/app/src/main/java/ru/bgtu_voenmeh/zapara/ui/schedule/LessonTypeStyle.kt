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
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.LessonTypeKind
import ru.bgtu_voenmeh.zapara.data.LessonTypeKinds
import ru.bgtu_voenmeh.zapara.ui.theme.TypeInks
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

object LessonTypeStyle {
    fun kind(type: String): LessonTypeKind? = LessonTypeKinds.of(type)

    fun labelRes(kind: LessonTypeKind): Int = when (kind) {
        LessonTypeKind.Lecture -> R.string.type_chip_lecture
        LessonTypeKind.Practice -> R.string.type_chip_practice
        LessonTypeKind.Lab -> R.string.type_chip_lab
        LessonTypeKind.Consult -> R.string.type_chip_consult
        LessonTypeKind.Credit -> R.string.type_chip_credit
        LessonTypeKind.Exam -> R.string.type_chip_exam
        LessonTypeKind.Course -> R.string.type_chip_course
    }

    fun ink(kind: LessonTypeKind, dark: Boolean): Color = TypeInks.of(kind, dark)

    fun wash(kind: LessonTypeKind, dark: Boolean): Color = ink(kind, dark).copy(alpha = 0.18f)
}

@Composable
fun LessonTypeChip(type: String, tag: String, modifier: Modifier = Modifier) {
    if (type.isBlank()) return
    val kind = LessonTypeStyle.kind(type)
    val dark = Zapara.colors.isDark
    val ink = kind?.let { LessonTypeStyle.ink(it, dark) } ?: Zapara.colors.text2
    val wash = kind?.let { LessonTypeStyle.wash(it, dark) } ?: Zapara.colors.chip
    val label = kind?.let { stringResource(LessonTypeStyle.labelRes(it)) } ?: type
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
