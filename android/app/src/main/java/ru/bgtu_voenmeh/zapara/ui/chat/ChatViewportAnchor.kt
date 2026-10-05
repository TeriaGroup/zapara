package ru.bgtu_voenmeh.zapara.ui.chat

import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.snapshotFlow
import androidx.compose.runtime.withFrameNanos
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.yield

/** Keep the latest message visible when the keyboard/composer changes the history height.
 * A reader away from the end retains LazyColumn's normal key/offset anchor.
 */
@Composable
internal fun KeepLatestVisible(list: LazyListState, conversationKey: Any?, enabled: Boolean = true) {
    LaunchedEffect(list, conversationKey, enabled) {
        if (!enabled) return@LaunchedEffect
        var previous: ChatViewport? = null
        snapshotFlow {
            val layout = list.layoutInfo
            ChatViewport(layout.viewportSize.height, layout.totalItemsCount,
                layout.visibleItemsInfo.lastOrNull()?.index ?: -1, list.isScrollInProgress)
        }.collect { current ->
            val before = previous
            previous = current
            if (before != null && shouldKeepLatestOnResize(before, current)) {
                val anchorIndex = list.firstVisibleItemIndex
                val anchorOffset = list.firstVisibleItemScrollOffset
                // layoutInfo can publish from inside a measure pass. Scrolling there forces
                // a nested remeasure; let that frame finish before moving the viewport.
                withFrameNanos { }
                yield()
                val latest = list.layoutInfo
                if (latest.viewportSize.height > 0 && latest.totalItemsCount == current.count &&
                    !list.isScrollInProgress && list.firstVisibleItemIndex == anchorIndex &&
                    list.firstVisibleItemScrollOffset == anchorOffset) {
                    list.scrollToItem(latest.totalItemsCount - 1)
                }
            }
        }
    }
}

internal data class ChatViewport(val height: Int, val count: Int, val lastVisible: Int, val scrolling: Boolean)

internal fun shouldKeepLatestOnResize(before: ChatViewport, after: ChatViewport): Boolean =
    before.height > 0 && after.height > 0 && before.height != after.height &&
        before.count > 0 && after.count > 0 && before.lastVisible >= before.count - 2 &&
        !before.scrolling && !after.scrolling
