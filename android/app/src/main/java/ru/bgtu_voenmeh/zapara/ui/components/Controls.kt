package ru.bgtu_voenmeh.zapara.ui.components

import androidx.annotation.DrawableRes
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.clickable
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.ui.platform.LocalDensity
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
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.window.DialogWindowProvider
import android.view.WindowManager
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
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
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
    tag: String? = null,
    textStyle: TextStyle = Zapara.typography.caption
) {
    val c = Zapara.colors
    val source = remember { MutableInteractionSource() }
    val shape = RoundedCornerShape(Zapara.radii.chip)
    Row(
        modifier
            .then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .sizeIn(minWidth = if (onClick != null) Zapara.space.minTouch else 0.dp, minHeight = Zapara.space.minTouch)
            .then(if (onClick != null) Modifier.pressScale(source) else Modifier)
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
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs, Alignment.CenterHorizontally)
    ) {
        if (leading != null) leading()
        Text(
            text,
            style = textStyle,
            color = if (selected) c.onAccent else c.text1
        )
    }
}

@Composable
fun ZSegmented(items: List<String>, selected: Int, onSelect: (Int) -> Unit, tag: String, modifier: Modifier = Modifier) {
    val c = Zapara.colors
    val vertical = LocalDensity.current.fontScale >= 1.5f
    val container = modifier.testTag(tag).selectableGroup()
        .clip(RoundedCornerShape(Zapara.radii.control)).background(c.chip).padding(Zapara.space.xs)
    @Composable fun segment(index: Int, label: String, itemModifier: Modifier) {
        val active = index == selected
        val interactions = remember { MutableInteractionSource() }
        Box(
            itemModifier
                .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
                .testTag("$tag.$index")
                .clip(RoundedCornerShape(Zapara.radii.control))
                .background(if (active) c.segThumb else Color.Transparent)
                .selectable(selected = active, role = Role.Tab, interactionSource = interactions,
                    indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                    onClick = { onSelect(index) })
                .padding(Zapara.space.s),
            contentAlignment = Alignment.Center
        ) {
            Text(
                label,
                style = if (active) Zapara.typography.bodyStrong else Zapara.typography.caption,
                color = if (active) c.text1 else c.text2
            )
        }
    }
    if (vertical) {
        Column(container) { items.forEachIndexed { index, label -> segment(index, label, Modifier.fillMaxWidth()) } }
    } else {
        Row(container.height(IntrinsicSize.Min)) {
            items.forEachIndexed { index, label -> segment(index, label, Modifier.weight(1f).fillMaxHeight()) }
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
fun ZBottomSheet(onDismiss: () -> Unit, tag: String, scrollable: Boolean = false, content: @Composable ColumnScope.() -> Unit) {
    val c = Zapara.colors
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)
    ) {
    val window = (LocalView.current.parent as DialogWindowProvider).window
    SideEffect {
        window.setLayout(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT)
        window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_NOTHING)
    }
    BackHandler(onBack = onDismiss)
    Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
        Box(
            Modifier
                .fillMaxSize()
                .background(c.backdrop)
                .clickable(onClick = onDismiss)
        )
        BoxWithConstraints(
            Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .systemBarsPadding()
                .imePadding()
        ) {
            Column(
                Modifier
                    .fillMaxWidth()
                    .heightIn(max = maxHeight)
                    .clip(RoundedCornerShape(topStart = Zapara.radii.dialog, topEnd = Zapara.radii.dialog))
                    .background(c.surface)
                    .clickable(
                        indication = null,
                        interactionSource = remember { MutableInteractionSource() }
                    ) {}
                    .padding(Zapara.space.l)
                    .testTag(tag)
            ) {
                Box(
                    Modifier
                        .align(Alignment.CenterHorizontally)
                        .padding(bottom = Zapara.space.s)
                        .size(width = 28.dp, height = 3.dp)
                        .clip(RoundedCornerShape(Zapara.radii.pill))
                        .background(c.lineStrong)
                )
                if (scrollable) {
                    Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()), content = content)
                } else content()
            }
        }
    }
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
