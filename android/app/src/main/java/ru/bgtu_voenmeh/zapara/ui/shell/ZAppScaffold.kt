package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

/** One inset owner for conversation screens, also used by the offline keyboard fixture. */
@Composable
internal fun ZAppScaffold(
    modifier: Modifier = Modifier,
    conversation: Boolean,
    bottomBar: @Composable () -> Unit,
    content: @Composable BoxScope.() -> Unit
) {
    val keyboardVisible = WindowInsets.ime.getBottom(LocalDensity.current) > 0
    Scaffold(modifier = modifier, containerColor = Zapara.colors.canvas,
        bottomBar = { if (!conversation || !keyboardVisible) bottomBar() }) { padding ->
        val frame = Modifier.padding(padding).consumeWindowInsets(padding)
        Box((if (conversation) frame.imePadding() else frame).fillMaxSize(), content = content)
    }
}
