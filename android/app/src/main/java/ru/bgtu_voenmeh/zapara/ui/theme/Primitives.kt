package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.pressScale

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ZCard(
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
    onLongClick: (() -> Unit)? = null,
    tag: String? = null,
    padded: Boolean = true,
    content: @Composable ColumnScope.() -> Unit
) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val c = Zapara.colors
    val clickable = if (onClick != null || onLongClick != null) {
        Modifier.pressScale(source).combinedClickable(
            interactionSource = source,
            indication = if (Zapara.motion.enabled) LocalIndication.current else null,
            onClick = { onClick?.invoke() },
            onLongClick = onLongClick
        )
    } else Modifier
    Surface(
        modifier
            .then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .then(clickable),
        shape = RoundedCornerShape(Zapara.radii.card),
        color = if (pressed) c.cardPressed else c.card,
        contentColor = c.text1,
        border = BorderStroke(Zapara.space.hairline, c.line)
    ) {
        Column(
            if (padded) Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.m) else Modifier,
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s),
            content = content
        )
    }
}

@Composable
fun ZButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, ghost: Boolean = false, tag: String? = null, quiet: Boolean = false, busy: Boolean = false, @DrawableRes leadingIcon: Int? = null) {
    val c = Zapara.colors
    val shape = RoundedCornerShape(Zapara.radii.control)
    val interactions = remember { MutableInteractionSource() }
    val focused by interactions.collectIsFocusedAsState()
    val pressed by interactions.collectIsPressedAsState()
    val busyDescription = stringResource(R.string.ux60_busy_operation, text)
    val restingColor = if (!ghost) c.accent else if (quiet) Color.Transparent else c.chip
    val contentColor = if (!enabled) c.text2 else if (ghost) c.text1 else c.onAccent
    val backgroundColor = when {
        !enabled && !ghost -> c.accent.copy(alpha = 0.12f)
        !enabled -> if (quiet) Color.Transparent else c.chip.copy(alpha = 0.5f)
        pressed && !busy -> (if (ghost) c.selection else c.onAccent.copy(alpha = 0.12f)).compositeOver(restingColor)
        else -> restingColor
    }
    Surface(
        modifier.then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .then(if (busy) Modifier.semantics(mergeDescendants = true) {
                contentDescription = text
                stateDescription = busyDescription
            } else Modifier)
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .pressScale(interactions)
            .clip(shape).controlFocusRing(enabled && !busy && focused, contentColor, Zapara.radii.control).clickable(
                interactionSource = interactions,
                indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                enabled = enabled && !busy, role = Role.Button, onClick = onClick),
        shape = shape,
        color = backgroundColor,
        contentColor = contentColor
    ) {
        Box(Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s), contentAlignment = Alignment.Center) {
            Box(contentAlignment = Alignment.Center) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s, Alignment.CenterHorizontally),
                    modifier = if (busy) Modifier.alpha(0f).clearAndSetSemantics { } else Modifier)
                {
                    if (leadingIcon != null) Icon(painterResource(leadingIcon), null, Modifier.size(Zapara.space.icon), tint = contentColor)
                    Text(text, style = Zapara.typography.bodyStrong, textAlign = TextAlign.Center, modifier = Modifier.weight(1f, fill = false))
                }
                if (busy) Box(Modifier.matchParentSize(), contentAlignment = Alignment.Center) {
                    Text(androidx.compose.ui.res.stringResource(ru.bgtu_voenmeh.zapara.R.string.space_day_busy), style = Zapara.typography.bodyStrong,
                        maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.clearAndSetSemantics { })
                }
            }
        }
    }
}

internal fun Modifier.controlFocusRing(focused: Boolean, color: Color, radius: Dp): Modifier = drawWithContent {
    drawContent()
    if (focused) {
        val width = (ZaparaSpace.hairline * 2).toPx()
        val inset = ZaparaSpace.xs.toPx() + width / 2
        drawRoundRect(color, Offset(inset, inset), Size((size.width - inset * 2).coerceAtLeast(0f), (size.height - inset * 2).coerceAtLeast(0f)),
            CornerRadius((radius.toPx() - inset).coerceAtLeast(0f)), style = Stroke(width))
    }
}

@Composable
fun ZIcon(@DrawableRes icon: Int, description: String?, modifier: Modifier = Modifier) {
    Icon(painterResource(icon), description, modifier.size(Zapara.space.icon), tint = Zapara.colors.text1)
}

@Composable
fun ZIconButton(
    @DrawableRes icon: Int,
    contentDescription: String,
    onClick: () -> Unit,
    tag: String,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    primary: Boolean = false
) {
    val source = remember { MutableInteractionSource() }
    val focused by source.collectIsFocusedAsState()
    val pressed by source.collectIsPressedAsState()
    val c = Zapara.colors
    val contentColor = if (!enabled) c.text2 else if (primary) c.onAccent else c.text1
    val restingColor = if (primary) c.accent else c.chip
    val backgroundColor = when {
        !enabled -> if (primary) c.accent.copy(alpha = 0.12f) else c.chip.copy(alpha = 0.5f)
        pressed -> (if (primary) c.onAccent.copy(alpha = 0.12f) else c.selection).compositeOver(restingColor)
        else -> restingColor
    }
    val accessibleName = contentDescription
    Box(
        modifier
            .testTag(tag)
            .semantics { this.contentDescription = accessibleName }
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .then(if (enabled) Modifier.pressScale(source) else Modifier)
            .clip(RoundedCornerShape(Zapara.radii.control))
            .controlFocusRing(enabled && focused, contentColor, Zapara.radii.control)
            .background(backgroundColor)
            .clickable(enabled = enabled, interactionSource = source, indication = if (Zapara.motion.enabled) LocalIndication.current else null, role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Icon(painterResource(icon), null, Modifier.size(Zapara.space.icon), tint = contentColor)
    }
}
