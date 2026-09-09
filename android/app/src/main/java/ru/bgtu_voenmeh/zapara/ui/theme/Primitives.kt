package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.LocalIndication
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
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import ru.bgtu_voenmeh.zapara.ui.components.pressScale

/** Foundation only; shell-specific controls belong to Task2. */
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
fun ZButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier, enabled: Boolean = true, ghost: Boolean = false, tag: String? = null) {
    val c = Zapara.colors
    val shape = RoundedCornerShape(Zapara.radii.control)
    val interactions = remember { MutableInteractionSource() }
    val focused by interactions.collectIsFocusedAsState()
    val border = if (ghost) BorderStroke(Zapara.space.hairline, c.lineStrong) else null
    val focusGap = Zapara.space.xs
    val focusWidth = Zapara.space.hairline * 2
    val controlRadius = Zapara.radii.control
    Surface(
        modifier.then(if (tag != null) Modifier.testTag(tag) else Modifier).sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .pressScale(interactions)
            .clip(shape).drawWithContent {
                drawContent()
                // Inset contrast survives reduced motion and does not move content or the touch target.
                if (enabled && focused) {
                    val width = focusWidth.toPx()
                    val inset = focusGap.toPx() + width / 2
                    val radius = (controlRadius.toPx() - inset).coerceAtLeast(0f)
                    drawRoundRect(
                        color = if (ghost) c.text1 else c.onAccent,
                        topLeft = Offset(inset, inset),
                        size = Size(size.width - inset * 2, size.height - inset * 2),
                        cornerRadius = CornerRadius(radius), style = Stroke(width)
                    )
                }
            }.clickable(
                interactionSource = interactions,
                indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                enabled = enabled, role = Role.Button, onClick = onClick),
        shape = shape, border = border,
        color = if (!enabled) c.accent.copy(alpha = 0.45f) else if (ghost) Color.Transparent else c.accent,
        contentColor = if (!enabled) c.onAccent.copy(alpha = 0.45f) else if (ghost) c.text1 else c.onAccent
    ) {
        Box(Modifier.padding(horizontal = Zapara.space.l, vertical = Zapara.space.s), contentAlignment = Alignment.Center) {
            Text(text, style = Zapara.typography.bodyStrong)
        }
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
    modifier: Modifier = Modifier
) {
    val source = remember { MutableInteractionSource() }
    Box(
        modifier
            .testTag(tag)
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .pressScale(source)
            .clip(RoundedCornerShape(Zapara.radii.icon))
            .clickable(interactionSource = source, indication = if (Zapara.motion.enabled) LocalIndication.current else null, role = Role.Button, onClick = onClick),
        contentAlignment = Alignment.Center
    ) {
        ZIcon(icon, contentDescription)
    }
}
