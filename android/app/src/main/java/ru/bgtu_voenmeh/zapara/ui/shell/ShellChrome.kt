package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.annotation.DrawableRes
import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.pressScale
import androidx.compose.material3.Icon
import androidx.compose.ui.res.painterResource
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.IndicatorXKey
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton

data class ShellChrome(
    val chip: String?,
    val stale: Boolean,
    val hasGroup: Boolean,
    val onGroupChip: () -> Unit
)

val LocalShellChrome = staticCompositionLocalOf { ShellChrome(null, false, false) {} }

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ZTopBar(title: String, actions: @Composable RowScope.() -> Unit = {}) {
    val container = Modifier.fillMaxWidth().heightIn(min = 56.dp)
        .background(Zapara.colors.canvas).padding(horizontal = Zapara.space.l)
    if (LocalDensity.current.fontScale >= 1.5f) {
        Column(container) {
            Text(title, style = Zapara.typography.title, color = Zapara.colors.text1,
                modifier = Modifier.fillMaxWidth().testTag("Top.Title"))
            FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                Row(verticalAlignment = Alignment.CenterVertically, content = actions)
                GroupChip()
            }
        }
    } else {
        Row(container, verticalAlignment = Alignment.CenterVertically) {
            Text(title, style = Zapara.typography.title, color = Zapara.colors.text1,
                modifier = Modifier.weight(1f).testTag("Top.Title"))
            actions()
            GroupChip()
        }
    }
}

@Composable
private fun GroupChip() {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    if (chrome.hasGroup && chrome.chip != null) {
        ZChip(
            text = chrome.chip,
            onClick = chrome.onGroupChip,
            tag = "Top.GroupChip",
            leading = if (chrome.stale) {
                {
                    Box(
                        Modifier
                            .size(6.dp)
                            .clip(CircleShape)
                            .background(c.warn)
                    )
                }
            } else null
        )
    } else {
        ZButton(
            text = stringResource(R.string.group_pick),
            onClick = chrome.onGroupChip,
            ghost = true,
            modifier = Modifier.testTag("Top.GroupChip")
        )
    }
}

@Composable
fun ZBottomBar(
    current: Section,
    sectionsActive: Boolean,
    homeworkBadge: Int,
    updateBadge: Boolean,
    onSection: (Section) -> Unit,
    onSections: () -> Unit
) {
    val c = Zapara.colors
    val motion = Zapara.motion
    val activeIndex = if (sectionsActive) 3 else Section.bar.indexOf(current).coerceAtLeast(0)
    Column(
        Modifier
            .fillMaxWidth()
            .background(c.surface)
            .navigationBarsPadding()
    ) {
        HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            val columns = if (LocalDensity.current.fontScale >= 1.5f) 2 else 4
            val cell = maxWidth / columns
            val target = cell * (activeIndex % columns) + (cell - 18.dp) / 2
            val offsetX by animateDpAsState(targetValue = target, animationSpec = tween(motion.ms(Durations.indicator), easing = ZaparaEase), label = "indicator")
            Column {
                (0..3).toList().chunked(columns).forEach { row ->
                    Row(Modifier.fillMaxWidth().height(IntrinsicSize.Min)) {
                        row.forEach { index ->
                            val section = Section.bar.getOrNull(index)
                            BarItem(
                                modifier = Modifier.weight(1f).fillMaxHeight(),
                                iconRes = section?.icon ?: R.drawable.ic_menu,
                                label = stringResource(section?.title ?: R.string.nav_sections),
                                active = index == activeIndex,
                                badge = if (section == Section.Homework && homeworkBadge > 0) homeworkBadge.toString() else null,
                                dot = section == null && updateBadge,
                                tag = section?.tag ?: "Nav.Sections",
                                indicatorX = offsetX,
                                indicatorShift = offsetX - target,
                                onClick = { if (section != null) onSection(section) else onSections() }
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun BarItem(
    modifier: Modifier,
    @DrawableRes iconRes: Int,
    label: String,
    active: Boolean,
    badge: String?,
    dot: Boolean,
    tag: String,
    indicatorX: Dp,
    indicatorShift: Dp,
    onClick: () -> Unit
) {
    val c = Zapara.colors
    val source = remember { MutableInteractionSource() }
    TextButton(
        onClick = onClick,
        modifier = modifier.heightIn(min = 64.dp).testTag(tag).semantics { selected = active }.pressScale(source),
        interactionSource = source,
        contentPadding = PaddingValues(0.dp)
    ) {
        Box(Modifier.fillMaxWidth()) {
            Column(
                Modifier.fillMaxWidth().padding(top = Zapara.space.s, bottom = Zapara.space.l),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center
            ) {
                Box(contentAlignment = Alignment.TopEnd) {
                    Icon(
                        painterResource(iconRes),
                        null,
                        modifier = Modifier.size(22.dp),
                        tint = if (active) c.text1 else c.text3
                    )
                    if (badge != null) {
                        Box(
                            Modifier
                                .align(Alignment.TopEnd)
                                .sizeIn(minWidth = 16.dp, minHeight = 16.dp)
                                .clip(CircleShape)
                                .background(c.bad),
                            contentAlignment = Alignment.Center
                        ) {
                            Text(badge, style = Zapara.typography.caption, color = c.onBad, maxLines = 1)
                        }
                    } else if (dot) {
                        Box(
                            Modifier
                                .align(Alignment.TopEnd)
                                .size(6.dp)
                                .clip(CircleShape)
                                .background(c.warn)
                        )
                    }
                }
                Text(
                    label,
                    style = Zapara.typography.caption,
                    color = if (active) c.text1 else c.text3
                )
                Spacer(Modifier.height(4.dp))
            }
            if (active) Box(Modifier.align(Alignment.BottomCenter).padding(bottom = Zapara.space.s)
                .offset(x = indicatorShift).size(width = 18.dp, height = 2.dp)
                .clip(RoundedCornerShape(Zapara.radii.pill)).background(c.text1)
                .testTag("Nav.Indicator").semantics { set(IndicatorXKey, indicatorX.value) })
        }
    }
}
