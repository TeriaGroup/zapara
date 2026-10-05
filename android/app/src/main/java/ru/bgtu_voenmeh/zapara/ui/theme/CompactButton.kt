package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.ui.components.pressScale

/** Compact inline action: its visible surface is smaller than its 48 dp touch target. */
@Composable
fun ZCompactButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    tag: String? = null,
    @DrawableRes leadingIcon: Int? = null
) {
    val c = Zapara.colors
    val shape = RoundedCornerShape(Zapara.radii.control)
    val interactions = remember { MutableInteractionSource() }
    val focused by interactions.collectIsFocusedAsState()
    val pressed by interactions.collectIsPressedAsState()
    Box(
        modifier.then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .then(if (enabled) Modifier.pressScale(interactions) else Modifier)
            .clip(shape)
            .clickable(enabled = enabled, interactionSource = interactions,
                indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                role = Role.Button, onClick = onClick)
            .padding(vertical = 6.dp),
        contentAlignment = Alignment.Center
    ) {
        Surface(
            modifier = Modifier.heightIn(min = 36.dp)
                .controlFocusRing(enabled && focused, c.focusRing, Zapara.radii.control),
            shape = shape,
            color = if (enabled && pressed) c.cardPressed else c.card,
            contentColor = if (enabled) c.text1 else c.text2,
            border = BorderStroke(Zapara.space.hairline, c.line)
        ) {
            Row(Modifier.padding(horizontal = Zapara.space.xs, vertical = Zapara.space.xs),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs, Alignment.CenterHorizontally)) {
                if (leadingIcon != null) Icon(painterResource(leadingIcon), null,
                    Modifier.size(18.dp), tint = c.text2)
                Text(text, style = Zapara.typography.bodyStrong, textAlign = TextAlign.Center,
                    modifier = Modifier.weight(1f, fill = false))
            }
        }
    }
}
