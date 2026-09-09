package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.platform.LocalContext
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.LocalUiCopy

enum class ThemeChoice(val key: String) {
    System("system"), Light("light"), Dark("dark");

    fun isDark(systemDark: Boolean): Boolean = when (this) {
        System -> systemDark
        Light -> false
        Dark -> true
    }

    companion object {
        fun fromKey(key: String?): ThemeChoice = entries.firstOrNull { it.key == key } ?: System
    }
}

private val LocalColors = staticCompositionLocalOf { DarkColors }

object Zapara {
    val colors: ZaparaColors @Composable get() = LocalColors.current
    val motion: MotionSettings @Composable get() = LocalMotion.current
    val typography get() = ZaparaType
    val type get() = ZaparaType
    val space get() = ZaparaSpace
    val radii get() = ZaparaRadius
}

@Composable
fun ZaparaTheme(
    choice: ThemeChoice = ThemeChoice.System,
    motion: MotionSettings = MotionSettings.On,
    content: @Composable () -> Unit
) {
    val colors = if (choice.isDark(isSystemInDarkTheme())) DarkColors else LightColors
    val effectiveMotion = rememberMotionSettings(motion.enabled)
    val base = if (colors.isDark) darkColorScheme() else lightColorScheme()
    val scheme = base.copy(
        primary = colors.accent, onPrimary = colors.onAccent,
        primaryContainer = colors.chip, onPrimaryContainer = colors.text1,
        inversePrimary = colors.onAccent,
        secondary = colors.text1, onSecondary = colors.onAccent,
        secondaryContainer = colors.chip, onSecondaryContainer = colors.text1,
        tertiary = colors.text1, onTertiary = colors.onAccent,
        tertiaryContainer = colors.chip, onTertiaryContainer = colors.text1,
        background = colors.canvas, onBackground = colors.text1,
        surface = colors.surface, onSurface = colors.text1,
        surfaceVariant = colors.card, onSurfaceVariant = colors.text2,
        surfaceTint = colors.surface,
        inverseSurface = colors.text1, inverseOnSurface = colors.canvas,
        error = colors.bad, onError = colors.onBad,
        errorContainer = colors.badSoft, onErrorContainer = colors.bad,
        outline = colors.lineStrong, outlineVariant = colors.line, scrim = colors.backdrop
    )
    val ctx = LocalContext.current
    val copy = remember(ctx) { AndroidUiCopy(ctx) }
    CompositionLocalProvider(LocalColors provides colors, LocalMotion provides effectiveMotion, LocalUiCopy provides copy) {
        MaterialTheme(colorScheme = scheme, typography = ZaparaTypography, content = content)
    }
}
