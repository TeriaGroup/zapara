package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.compose.material3.Typography
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp
import ru.bgtu_voenmeh.zapara.R

val Inter = FontFamily(
    Font(R.font.inter_regular, FontWeight.Normal),
    Font(R.font.inter_medium, FontWeight.Medium),
    Font(R.font.inter_semibold, FontWeight.SemiBold)
)

object ZaparaType {
    private fun style(size: Int, weight: FontWeight) = TextStyle(
        fontFamily = Inter, fontWeight = weight, fontSize = size.sp, lineHeight = (size * 1.3).sp
    )
    val title = style(22, FontWeight.Medium)
    val section = style(17, FontWeight.Medium)
    val body = style(15, FontWeight.Normal)
    val bodyStrong = style(15, FontWeight.Medium)
    val caption = style(12, FontWeight.Normal)
}

val ZaparaTypography = Typography(
    displayLarge = ZaparaType.title, displayMedium = ZaparaType.title, displaySmall = ZaparaType.title,
    headlineLarge = ZaparaType.title, headlineMedium = ZaparaType.title, headlineSmall = ZaparaType.section,
    titleLarge = ZaparaType.title, titleMedium = ZaparaType.section, titleSmall = ZaparaType.bodyStrong,
    bodyLarge = ZaparaType.body, bodyMedium = ZaparaType.body, bodySmall = ZaparaType.caption,
    labelLarge = ZaparaType.bodyStrong, labelMedium = ZaparaType.caption, labelSmall = ZaparaType.caption
)
