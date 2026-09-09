package ru.bgtu_voenmeh.zapara.ui.components

import androidx.annotation.DrawableRes
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.activity.compose.BackHandler
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.ui.theme.Durations
import ru.bgtu_voenmeh.zapara.ui.theme.ShineXKey
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaEase
import ru.bgtu_voenmeh.zapara.ui.theme.rememberPulse
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZIcon

@Composable
fun Modifier.pressScale(interactionSource: MutableInteractionSource? = null): Modifier {
    val source = interactionSource ?: remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val motion = Zapara.motion
    val scale by animateFloatAsState(targetValue = if (motion.enabled && pressed) 0.98f else 1f, animationSpec = tween(motion.ms(Durations.press), easing = ZaparaEase), label = "pressScale")
    return this.scale(scale)
}

@Composable
fun ZChip(
    text: String,
    modifier: Modifier = Modifier,
    selected: Boolean = false,
    onClick: (() -> Unit)? = null,
    leading: (@Composable () -> Unit)? = null,
    tag: String? = null
) {
    val c = Zapara.colors
    val source = remember { MutableInteractionSource() }
    val shape = RoundedCornerShape(Zapara.radii.chip)
    Row(
        modifier
            .then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .sizeIn(minHeight = Zapara.space.minTouch)
            .pressScale(source)
            .clip(shape)
            .background(if (selected) c.accent else c.chip)
            .then(
                if (onClick != null) Modifier.clickable(
                    interactionSource = source,
                    indication = null,
                    role = Role.Button,
                    onClick = onClick
                ) else Modifier
            )
            .padding(horizontal = Zapara.space.s, vertical = Zapara.space.xs),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)
    ) {
        if (leading != null) leading()
        Text(
            text,
            style = Zapara.typography.caption,
            color = if (selected) c.onAccent else c.text1,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis
        )
    }
}

@Composable
fun ZSegmented(items: List<String>, selected: Int, onSelect: (Int) -> Unit, tag: String, modifier: Modifier = Modifier) {
    val c = Zapara.colors
    Row(
        modifier
            .testTag(tag)
            .height(36.dp)
            .clip(RoundedCornerShape(Zapara.radii.control))
            .background(c.chip)
            .padding(3.dp)
    ) {
        items.forEachIndexed { index, label ->
            val active = index == selected
            Box(
                Modifier
                    .weight(1f)
                    .fillMaxHeight()
                    .testTag("$tag.$index")
                    .clip(RoundedCornerShape(Zapara.radii.control))
                    .background(if (active) c.segThumb else Color.Transparent)
                    .clickable { onSelect(index) },
                contentAlignment = Alignment.Center
            ) {
                Text(
                    label,
                    style = if (active) Zapara.typography.bodyStrong else Zapara.typography.caption,
                    color = if (active) c.text1 else c.text2,
                    maxLines = 1
                )
            }
        }
    }
}

@Composable
fun ZSwitch(checked: Boolean, onCheckedChange: (Boolean) -> Unit, tag: String, modifier: Modifier = Modifier) {
    val c = Zapara.colors
    Switch(
        checked = checked,
        onCheckedChange = onCheckedChange,
        modifier = modifier
            .testTag(tag)
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch),
        colors = SwitchDefaults.colors(
            checkedTrackColor = c.accent,
            checkedThumbColor = c.onAccent,
            uncheckedTrackColor = c.chip,
            uncheckedThumbColor = c.text2,
            uncheckedBorderColor = c.lineStrong
        )
    )
}

@OptIn(ExperimentalComposeUiApi::class)
@Composable
fun ZBottomSheet(onDismiss: () -> Unit, tag: String, content: @Composable ColumnScope.() -> Unit) {
    val c = Zapara.colors
    BackHandler(onBack = onDismiss)
    Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
        Box(
            Modifier
                .fillMaxSize()
                .background(c.backdrop)
                .clickable(onClick = onDismiss)
        )
        Column(
            Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .clip(RoundedCornerShape(topStart = Zapara.radii.dialog, topEnd = Zapara.radii.dialog))
                .background(c.surface)
                .padding(Zapara.space.l)
                .testTag(tag),
            content = {
                Box(
                    Modifier
                        .align(Alignment.CenterHorizontally)
                        .padding(bottom = Zapara.space.s)
                        .size(width = 28.dp, height = 3.dp)
                        .clip(RoundedCornerShape(Zapara.radii.pill))
                        .background(c.lineStrong)
                )
                content()
            }
        )
    }
}

@Composable
fun EmptyState(
    @DrawableRes icon: Int?,
    title: String,
    hint: String? = null,
    actionText: String? = null,
    onAction: (() -> Unit)? = null,
    tag: String = "Empty",
    modifier: Modifier = Modifier
) {
    val c = Zapara.colors
    Column(
        modifier.fillMaxSize().testTag(tag).padding(Zapara.space.l),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        if (icon != null) ZIcon(icon, null, Modifier.size(28.dp))
        Spacer(Modifier.height(Zapara.space.s))
        Text(title, style = Zapara.typography.section, color = c.text1)
        if (hint != null) {
            Spacer(Modifier.height(Zapara.space.xs))
            Text(hint, style = Zapara.typography.caption, color = c.text2)
        }
        if (actionText != null && onAction != null) {
            Spacer(Modifier.height(Zapara.space.m))
            ZButton(actionText, onAction, ghost = true)
        }
    }
}

@Composable
fun Skeleton(modifier: Modifier = Modifier, height: Dp = 72.dp) {
    val motion = Zapara.motion
    val progress by rememberPulse(active = motion.enabled, cycleMs = Durations.skeleton, from = -1f, to = 1f)
    BoxWithConstraints(
        modifier
            .fillMaxWidth()
            .height(height)
            .clip(RoundedCornerShape(Zapara.radii.card))
            .background(Zapara.colors.chip)
            .semantics { set(ShineXKey, progress); stateDescription = progress.toString(); contentDescription = progress.toString() }
    ) {
        if (motion.enabled) {
            val parentPx = constraints.maxWidth.toFloat()
            Box(
                Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(0.4f)
                    .graphicsLayer { translationX = progress * parentPx }
                    .background(Zapara.colors.lineStrong)
            )
        }
    }
}

@Composable
fun SkeletonList(count: Int = 3) {
    Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        repeat(count) { Skeleton() }
    }
}

@Composable
fun FriendDot(index: Int, modifier: Modifier = Modifier, size: Dp = 8.dp) {
    val color = Zapara.colors.friends.getOrElse(index.mod(Zapara.colors.friends.size)) { Zapara.colors.text2 }
    Box(modifier.size(size).clip(CircleShape).background(color))
}

@Composable
fun HighlightText(text: String, query: String, style: TextStyle = Zapara.typography.body, modifier: Modifier = Modifier) {
    val c = Zapara.colors
    val q = query.trim()
    if (q.isEmpty()) {
        Text(text, style = style, color = c.text1, modifier = modifier)
        return
    }
    val start = text.indexOf(q, ignoreCase = true)
    if (start < 0) {
        Text(text, style = style, color = c.text1, modifier = modifier)
        return
    }
    val annotated = buildAnnotatedString {
        append(text.substring(0, start))
        withStyle(SpanStyle(background = c.selection, color = c.text1)) {
            append(text.substring(start, start + q.length))
        }
        append(text.substring(start + q.length))
    }
    Text(annotated, style = style, modifier = modifier)
}
