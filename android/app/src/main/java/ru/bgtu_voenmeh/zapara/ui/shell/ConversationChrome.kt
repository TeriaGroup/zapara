package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.staticCompositionLocalOf

/** #108 / AN-17: экран беседы сообщает оболочке, что беседа открыта, — тогда нижняя панель скрыта. */
val LocalConversationOpen = staticCompositionLocalOf<(Boolean) -> Unit> { {} }

@Composable
fun ReportConversationOpen(open: Boolean) {
    val report = LocalConversationOpen.current
    DisposableEffect(open, report) {
        report(open)
        onDispose { report(false) }
    }
}
