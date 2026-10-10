package ru.bgtu_voenmeh.zapara.design

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Density
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ZAppScaffold
import ru.bgtu_voenmeh.zapara.ui.shell.ZBottomBar
import ru.bgtu_voenmeh.zapara.ui.theme.MotionSettings
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

@Composable
fun Shell(
    dark: Boolean,
    section: Section?,
    chip: String? = "И831Б · нечёт",
    hasGroup: Boolean = true,
    stale: Boolean = false,
    homeworkBadge: Int = 0,
    fontScale: Float = 1f,
    content: @Composable () -> Unit
) {
    val d = LocalDensity.current
    // Как ZaparaApp (#108 / AN-17): открытая беседа сообщает о себе своим токеном, и нижняя панель скрывается.
    val conversation = remember { ru.bgtu_voenmeh.zapara.ui.shell.ConversationOpenState() }
    CompositionLocalProvider(LocalDensity provides Density(d.density, fontScale)) {
        ZaparaTheme(choice = if (dark) ThemeChoice.Dark else ThemeChoice.Light, motion = MotionSettings(false, 0f)) {
            CompositionLocalProvider(LocalShellChrome provides ShellChrome(chip, stale, hasGroup) {}) {
                if (section == null) Box(Modifier.fillMaxSize()) { content() }
                else ZAppScaffold(conversation = section == Section.Chat || section == Section.Group,
                    bottomBar = {
                        if (ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic.showBottomBar(section, conversation.open))
                            ZBottomBar(current = section, sectionsActive = section !in Section.bar,
                                homeworkBadge = homeworkBadge, updateBadge = false, onSection = {}, onSections = {})
                    }) { CompositionLocalProvider(ru.bgtu_voenmeh.zapara.ui.shell.LocalConversationOpen provides conversation) { content() } }
            }
        }
    }
}
