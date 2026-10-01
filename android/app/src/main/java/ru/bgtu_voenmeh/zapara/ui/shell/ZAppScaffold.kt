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
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

/** One inset owner for conversation screens, also used by the offline keyboard fixture. */
@Composable
internal fun ZAppScaffold(
    modifier: Modifier = Modifier,
    conversation: Boolean,
    bottomBar: @Composable () -> Unit,
    content: @Composable BoxScope.() -> Unit
) {
    val keyboardVisible = rememberKeyboardVisible()
    Scaffold(modifier = modifier, containerColor = Zapara.colors.canvas,
        bottomBar = { if (!keyboardVisible) bottomBar() }) { padding ->
        val frame = Modifier.padding(padding).consumeWindowInsets(padding)
        Box((if (conversation) frame.imePadding() else frame).fillMaxSize(), content = content)
    }
}

@Composable
internal fun rememberKeyboardVisible(): Boolean {
    val view = LocalView.current
    var platformKeyboardVisible by remember(view) { mutableStateOf(false) }
    DisposableEffect(view) {
        // adjustResize may consume the IME inset before it reaches Compose.
        val listener = android.view.ViewTreeObserver.OnGlobalLayoutListener {
            platformKeyboardVisible = ViewCompat.getRootWindowInsets(view)
                ?.isVisible(WindowInsetsCompat.Type.ime()) == true
        }
        view.viewTreeObserver.addOnGlobalLayoutListener(listener)
        listener.onGlobalLayout()
        onDispose { view.viewTreeObserver.removeOnGlobalLayoutListener(listener) }
    }
    return WindowInsets.ime.getBottom(LocalDensity.current) > 0 || platformKeyboardVisible
}
