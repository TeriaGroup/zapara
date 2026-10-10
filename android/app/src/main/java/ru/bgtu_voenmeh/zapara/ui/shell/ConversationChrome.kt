package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf

/**
 * #108 / AN-17: какие экраны сейчас держат беседу открытой. Каждый экран сообщает о себе своим токеном и снимает
 * только свой токен, поэтому dispose заменённого экрана (переход между беседами, анимация NavHost) не сбрасывает
 * флаг, поставленный новым экраном.
 */
class ConversationOpenState {
    private val owners = mutableStateListOf<Any>()
    val open: Boolean get() = owners.isNotEmpty()

    fun report(owner: Any, open: Boolean) {
        if (open) { if (owners.none { it === owner }) owners.add(owner) }
        else owners.removeAll { it === owner }
    }
}

val LocalConversationOpen = staticCompositionLocalOf { ConversationOpenState() }

@Composable
fun ReportConversationOpen(open: Boolean) {
    val state = LocalConversationOpen.current
    val owner = remember { Any() }
    DisposableEffect(open, state) {
        state.report(owner, open)
        onDispose { state.report(owner, false) }
    }
}
