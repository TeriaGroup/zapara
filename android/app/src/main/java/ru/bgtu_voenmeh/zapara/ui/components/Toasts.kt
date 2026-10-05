package ru.bgtu_voenmeh.zapara.ui.components

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.ui.platform.LocalAccessibilityManager
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import ru.bgtu_voenmeh.zapara.R
import java.util.concurrent.atomic.AtomicLong
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

enum class ToastKind { Plain, Ok, Bad }

data class ToastAction(val text: String, val run: () -> Unit)

data class Toast(val id: Long, val text: String, val kind: ToastKind, val action: ToastAction?)

class ToastCenter {
    private val mutable = MutableStateFlow<List<Toast>>(emptyList())
    val items: StateFlow<List<Toast>> = mutable.asStateFlow()
    private val nextId = AtomicLong(1L)

    fun show(text: String, kind: ToastKind = ToastKind.Plain, action: ToastAction? = null) {
        mutable.update { current ->
            val last = current.lastOrNull()
            val retained = if (last != null && last.text == text && last.kind == kind) current.dropLast(1) else current
            (retained + Toast(nextId.getAndIncrement(), text, kind, action)).takeLast(3)
        }
    }

    fun invokeAction(id: Long) {
        while (true) {
            val current = mutable.value
            val toast = current.firstOrNull { it.id == id } ?: return
            if (mutable.compareAndSet(current, current.filterNot { it.id == id })) {
                toast.action?.run?.invoke()
                return
            }
        }
    }

    fun dismiss(id: Long) {
        mutable.update { list -> list.filterNot { it.id == id } }
    }
}

@Composable
fun ToastHost(toasts: StateFlow<List<Toast>>, onDismiss: (Long) -> Unit, modifier: Modifier = Modifier,
    onAction: (Long) -> Unit = {}) {
    val items by toasts.collectAsStateWithLifecycle()
    val c = Zapara.colors
    val accessibility = LocalAccessibilityManager.current
    Column(
        modifier
            .fillMaxWidth()
            .padding(horizontal = 12.dp)
            .testTag("Toast"),
        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        items.forEach { toast ->
            key(toast.id) {
            val timeout = accessibility?.calculateRecommendedTimeoutMillis(4000,
                containsIcons = true, containsText = true, containsControls = true) ?: 4000L
            LaunchedEffect(toast.id, timeout) {
                delay(timeout.coerceAtLeast(4000L))
                onDismiss(toast.id)
            }
            val dot = when (toast.kind) {
                ToastKind.Plain -> c.text2
                ToastKind.Ok -> c.ok
                ToastKind.Bad -> c.bad
            }
            Row(
                Modifier
                    .fillMaxWidth()
                    .semantics { liveRegion = LiveRegionMode.Polite }
                    .clip(RoundedCornerShape(Zapara.radii.toast))
                    .background(c.card)
                    .border(Zapara.space.hairline, c.line, RoundedCornerShape(Zapara.radii.toast))
                    .padding(horizontal = Zapara.space.m, vertical = Zapara.space.s),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Box(Modifier.size(8.dp).clip(CircleShape).background(dot))
                Spacer(Modifier.width(Zapara.space.s))
                Text(toast.text, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
                if (toast.action != null) {
                    TextButton(onClick = { onAction(toast.id) }) {
                        Text(toast.action.text, style = Zapara.typography.caption, color = c.text1)
                    }
                }
                TextButton(onClick = { onDismiss(toast.id) }) {
                    Text(stringResource(R.string.ux60_feedback_dismiss), style = Zapara.typography.caption, color = c.text1)
                }
            }
            }
        }
    }
}
