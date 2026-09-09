package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.compose.runtime.Immutable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

@Immutable
data class ZaparaColors(
    val isDark: Boolean,
    val canvas: Color, val surface: Color, val card: Color, val cardPressed: Color,
    val chip: Color, val line: Color, val lineStrong: Color,
    val text1: Color, val text2: Color, val text3: Color,
    val accent: Color, val onAccent: Color, val selection: Color,
    val focusRing: Color, val backdrop: Color, val segThumb: Color,
    val ok: Color, val warn: Color, val warnSoft: Color, val onWarn: Color,
    val bad: Color, val badSoft: Color, val onBad: Color, val info: Color,
    val friends: List<Color>,
    val mapInk: Color = Color(0xFF2B7FD9),
    val mapInkSoft: Color = Color(0x402B7FD9),
    val onMapInk: Color = Color(0xFFFFFFFF),
    val qrPaper: Color = Color(0xFFFFFFFF),
    val closeHover: Color = Color(0xFFE81123)
)

val DarkColors = ZaparaColors(
    isDark = true,
    canvas = Color(0xFF0D0D0D), surface = Color(0xFF111111),
    card = Color(0xFF171717), cardPressed = Color(0xFF1C1C1C),
    chip = Color(0xFF222222), line = Color(0xFF262626), lineStrong = Color(0xFF333333),
    text1 = Color(0xFFF2F2F2), text2 = Color(0xFF8B8B8B), text3 = Color(0xFF5C5C5C),
    accent = Color(0xFFF2F2F2), onAccent = Color(0xFF0D0D0D), selection = Color(0x14FFFFFF),
    focusRing = Color(0x59FFFFFF), backdrop = Color(0x8C000000), segThumb = Color(0xFF171717),
    ok = Color(0xFF4CC38A), warn = Color(0xFFF2A33C), warnSoft = Color(0x24F2A33C), onWarn = Color(0xFF1A1A1A),
    bad = Color(0xFFEF5B6B), badSoft = Color(0x24EF5B6B), onBad = Color(0xFFFFFFFF), info = Color(0xFF5AA9FF),
    friends = listOf(Color(0xFFF2A33C), Color(0xFF4CC38A), Color(0xFF5AA9FF), Color(0xFFC77DFF), Color(0xFFFF7A9C))
)

val LightColors = ZaparaColors(
    isDark = false,
    canvas = Color(0xFFFFFFFF), surface = Color(0xFFF7F7F7),
    card = Color(0xFFF5F5F5), cardPressed = Color(0xFFEFEFEF),
    chip = Color(0xFFEBEBEB), line = Color(0xFFE5E5E5), lineStrong = Color(0xFFD4D4D4),
    text1 = Color(0xFF111111), text2 = Color(0xFF767676), text3 = Color(0xFFA3A3A3),
    accent = Color(0xFF111111), onAccent = Color(0xFFFFFFFF), selection = Color(0x0D000000),
    focusRing = Color(0x40000000), backdrop = Color(0x59000000), segThumb = Color(0xFFFFFFFF),
    ok = Color(0xFF2FA36B), warn = Color(0xFFD9861B), warnSoft = Color(0x1FD9861B), onWarn = Color(0xFFFFFFFF),
    bad = Color(0xFFD7404F), badSoft = Color(0x1FD7404F), onBad = Color(0xFFFFFFFF), info = Color(0xFF2B7FD9),
    friends = listOf(Color(0xFFD9861B), Color(0xFF2FA36B), Color(0xFF2B7FD9), Color(0xFF9B51E0), Color(0xFFE0527A))
)

object ZaparaSpace {
    val xs = 4.dp; val s = 8.dp; val m = 12.dp; val l = 16.dp; val xl = 24.dp
    val minTouch = 44.dp
    val hairline = 1.dp
    val icon = 24.dp
}

object ZaparaRadius {
    val card = 12.dp; val control = 8.dp; val chip = 6.dp; val pill = 999.dp
    val dialog = 14.dp; val icon = 9.dp; val toast = 10.dp
}
