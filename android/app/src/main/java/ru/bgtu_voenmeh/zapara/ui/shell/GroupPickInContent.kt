package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.compose.runtime.staticCompositionLocalOf

/**
 * #109 / AN-09: экран без группы сам предлагает «Выбрать группу» основной кнопкой — тогда шапка не дублирует
 * её второй кнопкой. Экран оборачивает шапку в CompositionLocalProvider(LocalGroupPickInContent provides true).
 */
val LocalGroupPickInContent = staticCompositionLocalOf { false }
