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

@Composable
fun ZTopBar(title: String, actions: @Composable RowScope.() -> Unit = {}) {
    val chrome = LocalShellChrome.current
    val c = Zapara.colors
    Row(
        Modifier
            .fillMaxWidth()
            .height(56.dp)
            .background(c.canvas)
            .padding(horizontal = Zapara.space.l),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            title,
            style = Zapara.typography.title,
            color = c.text1,
            modifier = Modifier.weight(1f).testTag("Top.Title"),
            maxLines = 1
        )
        actions()
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
        BoxWithConstraints(
            Modifier
                .fillMaxWidth()
                .height(64.dp)
        ) {
            val cell = maxWidth / 4
            val target = cell * activeIndex + (cell - 18.dp) / 2
            val offsetX by animateDpAsState(targetValue = target, animationSpec = tween(motion.ms(Durations.indicator), easing = ZaparaEase), label = "indicator")
            Row(Modifier.fillMaxSize()) {
                Section.bar.forEach { section ->
                    BarItem(
                        modifier = Modifier.weight(1f),
                        iconRes = section.icon,
                        label = stringResource(section.title),
                        active = current == section,
                        badge = if (section == Section.Homework && homeworkBadge > 0) homeworkBadge.toString() else null,
                        dot = false,
                        tag = section.tag,
                        onClick = { onSection(section) }
                    )
                }
                BarItem(
                    modifier = Modifier.weight(1f),
                    iconRes = R.drawable.ic_menu,
                    label = stringResource(R.string.nav_sections),
                    active = sectionsActive,
                    badge = null,
                    dot = updateBadge,
                    tag = "Nav.Sections",
                    onClick = onSections
                )
            }
            Box(
                Modifier
                    .align(Alignment.BottomStart)
                    .padding(bottom = 8.dp)
                    .offset(x = offsetX)
                    .size(width = 18.dp, height = 2.dp)
                    .clip(RoundedCornerShape(Zapara.radii.pill))
                    .background(c.text1)
                    .testTag("Nav.Indicator")
                    .semantics { set(IndicatorXKey, offsetX.value); stateDescription = offsetX.value.toString(); contentDescription = offsetX.value.toString() }
            )
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
    onClick: () -> Unit
) {
    val c = Zapara.colors
    val source = remember { MutableInteractionSource() }
    TextButton(
        onClick = onClick,
        modifier = modifier.fillMaxHeight().testTag(tag).pressScale(source),
        interactionSource = source,
        contentPadding = PaddingValues(0.dp)
    ) {
    Column(
        Modifier.fillMaxSize(),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center
    ) {
        Box(contentAlignment = Alignment.TopEnd) {
            Icon(
                painterResource(iconRes),
                label,
                modifier = Modifier.size(22.dp),
                tint = if (active) c.text1 else c.text3
            )
            if (badge != null) {
                Box(
                    Modifier
                        .align(Alignment.TopEnd)
                        .size(16.dp)
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
            color = if (active) c.text1 else c.text3,
            maxLines = 1
        )
        Spacer(Modifier.height(4.dp))
    }
    }
}
