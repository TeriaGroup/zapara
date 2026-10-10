package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.ui.draw.rotate
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
    /** Подпись действия по нажатию для TalkBack («Дважды нажмите, чтобы …»). */
    onClickLabel: String? = null,
    content: @Composable ColumnScope.() -> Unit
) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val c = Zapara.colors
    val clickable = if (onClick != null || onLongClick != null) {
        Modifier.pressScale(source).combinedClickable(
            interactionSource = source,
            indication = if (Zapara.motion.enabled) LocalIndication.current else null,
            onClickLabel = onClickLabel,
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
fun ZButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, ghost: Boolean = false, tag: String? = null, quiet: Boolean = false, busy: Boolean = false, @DrawableRes leadingIcon: Int? = null,
    startAligned: Boolean = false, @DrawableRes trailingIcon: Int? = null, trailingIconRotation: Float = 0f,
    destructive: Boolean = false) {
    val c = Zapara.colors
    val shape = RoundedCornerShape(Zapara.radii.control)
    val interactions = remember { MutableInteractionSource() }
    val focused by interactions.collectIsFocusedAsState()
    val pressed by interactions.collectIsPressedAsState()
    val busyDescription = stringResource(R.string.ux60_busy_operation, text)
    val secondary = ghost || destructive
    val restingColor = when {
        destructive -> c.badSoft
        !ghost -> c.accent
        quiet -> Color.Transparent
        else -> c.card
    }
    val contentColor = if (!enabled && !busy) c.text2 else if (secondary) c.text1 else c.onAccent
    val iconColor = when {
        !enabled && !busy -> c.text2
        destructive -> c.bad
        secondary -> c.text2
        else -> c.onAccent
    }
    val border = when {
        destructive -> BorderStroke(Zapara.space.hairline, if (enabled || busy) c.bad else c.line)
        ghost && !quiet -> BorderStroke(Zapara.space.hairline, c.line)
        else -> null
    }
    val backgroundColor = when {
        busy -> restingColor
        !enabled && !secondary -> c.accent.copy(alpha = 0.12f)
        !enabled -> if (quiet && !destructive) Color.Transparent else c.card
        pressed -> (if (secondary) c.selection else c.onAccent.copy(alpha = 0.12f)).compositeOver(restingColor)
        else -> restingColor
    }
    Surface(
        modifier.then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .then(if (busy) Modifier.semantics(mergeDescendants = true) {
                stateDescription = busyDescription
            } else Modifier)
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .then(if (enabled && !busy) Modifier.pressScale(interactions) else Modifier)
            .clip(shape).controlFocusRing(enabled && !busy && focused, contentColor, Zapara.radii.control).clickable(
                interactionSource = interactions,
                indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                enabled = enabled && !busy, role = Role.Button, onClick = onClick),
        shape = shape,
        color = backgroundColor,
        contentColor = contentColor,
        border = border
    ) {
        Box(contentAlignment = Alignment.Center) {
        Box(Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s), contentAlignment = Alignment.Center) {
            Box(contentAlignment = Alignment.Center) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(Zapara.space.s, Alignment.CenterHorizontally),
                    modifier = if (startAligned) Modifier.fillMaxWidth() else Modifier)
                {
                    if (leadingIcon != null) Icon(painterResource(leadingIcon), null, Modifier.size(Zapara.space.icon), tint = iconColor)
                    // #100: при fontScale 2.0 «Пользовательское соглашение» рвалось посреди слова; слово ужимается целиком.
                    ru.bgtu_voenmeh.zapara.ui.components.WordFitText(text, style = Zapara.typography.bodyStrong,
                        textAlign = if (startAligned) TextAlign.Start else TextAlign.Center,
                        modifier = Modifier.weight(1f, fill = startAligned))
                    if (trailingIcon != null) Icon(painterResource(trailingIcon), null,
                        Modifier.size(Zapara.space.icon - Zapara.space.xs).rotate(trailingIconRotation), tint = iconColor)
                }
            }
        }
        if (busy) Box(Modifier.matchParentSize()) {
            val progressModifier = Modifier.align(Alignment.BottomCenter).fillMaxWidth()
                .padding(horizontal = Zapara.space.s).padding(bottom = Zapara.space.xs)
                .height(Zapara.space.hairline * 2).clip(RoundedCornerShape(Zapara.radii.pill))
                .clearAndSetSemantics { }
            if (Zapara.motion.enabled) LinearProgressIndicator(modifier = progressModifier,
                color = contentColor, trackColor = contentColor.copy(alpha = 0.15f))
            else LinearProgressIndicator(progress = { 0.35f }, modifier = progressModifier,
                color = contentColor, trackColor = contentColor.copy(alpha = 0.15f))
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
    val restingColor = if (primary) c.accent else c.card
    val backgroundColor = when {
        !enabled -> if (primary) c.accent.copy(alpha = 0.12f) else c.card
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
            .then(if (!primary) Modifier.border(BorderStroke(Zapara.space.hairline, c.line),
                RoundedCornerShape(Zapara.radii.control)) else Modifier)
            .clickable(enabled = enabled, interactionSource = source, indication = if (Zapara.motion.enabled) LocalIndication.current else null, role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        Icon(painterResource(icon), null, Modifier.size(Zapara.space.icon), tint = contentColor)
    }
}
