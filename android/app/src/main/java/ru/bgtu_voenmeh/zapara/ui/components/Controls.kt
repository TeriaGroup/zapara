package ru.bgtu_voenmeh.zapara.ui.components

import androidx.annotation.DrawableRes
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.relocation.BringIntoViewRequester
import androidx.compose.foundation.relocation.bringIntoViewRequester
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.clickable
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.activity.compose.BackHandler
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.ProvideTextStyle
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.withFrameNanos
import androidx.compose.material3.LocalContentColor
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.window.DialogWindowProvider
import android.view.WindowManager
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.layout.Layout
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.text.input.VisualTransformation
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
import ru.bgtu_voenmeh.zapara.ui.theme.controlFocusRing
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.gestures.rememberSheetMotion
import ru.bgtu_voenmeh.zapara.ui.gestures.sheetDragHandle

@Composable
fun Modifier.pressScale(interactionSource: MutableInteractionSource? = null): Modifier {
    val source = interactionSource ?: remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val motion = Zapara.motion
    val scale by animateFloatAsState(targetValue = if (motion.enabled && pressed) 0.98f else 1f, animationSpec = tween(motion.ms(Durations.press), easing = ZaparaEase), label = "pressScale")
    return this.scale(scale)
}

@Composable
fun ZTextField(
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    label: (@Composable () -> Unit)? = null,
    placeholder: (@Composable () -> Unit)? = null,
    enabled: Boolean = true,
    readOnly: Boolean = false,
    singleLine: Boolean = false,
    minLines: Int = 1,
    maxLines: Int = if (singleLine) 1 else Int.MAX_VALUE,
    isError: Boolean = false,
    supportingText: (@Composable () -> Unit)? = null,
    visualTransformation: VisualTransformation = VisualTransformation.None,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
    leadingIcon: (@Composable () -> Unit)? = null,
    trailingIcon: (@Composable () -> Unit)? = null
) {
    val c = Zapara.colors
    OutlinedTextField(
        value = value, onValueChange = onValueChange, modifier = modifier,
        enabled = enabled, readOnly = readOnly, singleLine = singleLine,
        minLines = minLines, maxLines = maxLines, isError = isError,
        textStyle = Zapara.typography.body,
        label = label?.let { content -> { ProvideTextStyle(Zapara.typography.caption, content) } },
        placeholder = placeholder?.let { content -> { ProvideTextStyle(Zapara.typography.body, content) } },
        supportingText = supportingText?.let { content -> { ProvideTextStyle(Zapara.typography.caption, content) } },
        visualTransformation = visualTransformation,
        keyboardOptions = keyboardOptions, keyboardActions = keyboardActions,
        leadingIcon = leadingIcon, trailingIcon = trailingIcon,
        shape = RoundedCornerShape(Zapara.radii.control),
        colors = OutlinedTextFieldDefaults.colors(
            focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
            disabledContainerColor = c.chip.copy(alpha = 0.5f), errorContainerColor = c.chip,
            focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
            disabledBorderColor = Color.Transparent, errorBorderColor = c.bad,
            focusedTextColor = c.text1, unfocusedTextColor = c.text1,
            disabledTextColor = c.text2, errorTextColor = c.text1,
            cursorColor = c.text1, errorCursorColor = c.bad,
            focusedLabelColor = c.text2, unfocusedLabelColor = c.text2,
            disabledLabelColor = c.text2, errorLabelColor = c.bad,
            focusedPlaceholderColor = c.text2, unfocusedPlaceholderColor = c.text2,
            disabledPlaceholderColor = c.text2, errorPlaceholderColor = c.text2,
            focusedLeadingIconColor = c.text2, unfocusedLeadingIconColor = c.text2,
            disabledLeadingIconColor = c.text2, errorLeadingIconColor = c.text2,
            focusedTrailingIconColor = c.text2, unfocusedTrailingIconColor = c.text2,
            disabledTrailingIconColor = c.text2, errorTrailingIconColor = c.bad,
            focusedSupportingTextColor = c.text2, unfocusedSupportingTextColor = c.text2,
            disabledSupportingTextColor = c.text2, errorSupportingTextColor = c.bad
        )
    )
}

@Composable
fun ZChip(
    text: String,
    modifier: Modifier = Modifier,
    selected: Boolean = false,
    onClick: (() -> Unit)? = null,
    leading: (@Composable () -> Unit)? = null,
    tag: String? = null,
    textStyle: TextStyle = if (onClick != null) Zapara.typography.bodyStrong else Zapara.typography.caption
) {
    val c = Zapara.colors
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val focused by source.collectIsFocusedAsState()
    val radius = if (onClick != null) Zapara.radii.control else Zapara.radii.chip
    val shape = RoundedCornerShape(radius)
    val restingColor = if (selected) c.accent else c.chip
    val contentColor = if (selected) c.onAccent else c.text1
    Row(
        modifier
            .then(if (tag != null) Modifier.testTag(tag) else Modifier)
            .sizeIn(minWidth = if (onClick != null) Zapara.space.minTouch else 0.dp, minHeight = if (onClick != null) Zapara.space.minTouch else 0.dp)
            .then(if (onClick != null) Modifier.pressScale(source) else Modifier)
            .clip(shape)
            .background(if (pressed) contentColor.copy(alpha = 0.12f).compositeOver(restingColor) else restingColor)
            .controlFocusRing(onClick != null && focused, contentColor, radius)
            .then(if (onClick != null) Modifier.semantics { this.selected = selected } else Modifier)
            .then(
                if (onClick != null) Modifier.clickable(
                    interactionSource = source,
                    indication = if (Zapara.motion.enabled) LocalIndication.current else null,
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
            color = contentColor
        )
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ZSegmented(items: List<String>, selected: Int, onSelect: (Int) -> Unit, tag: String, modifier: Modifier = Modifier) {
    val c = Zapara.colors
    val container = modifier.testTag(tag).selectableGroup()
        .clip(RoundedCornerShape(Zapara.radii.control)).background(c.chip).padding(Zapara.space.xs)
    @Composable fun segment(index: Int, label: String, itemModifier: Modifier) {
        val active = index == selected
        val bring = remember { BringIntoViewRequester() }
        LaunchedEffect(active, LocalDensity.current.fontScale) {
            if (active) { withFrameNanos { }; bring.bringIntoView() }
        }
        val interactions = remember { MutableInteractionSource() }
        val focused by interactions.collectIsFocusedAsState()
        val pressed by interactions.collectIsPressedAsState()
        val restingColor = if (active) c.segThumb else Color.Transparent
        Box(
            itemModifier
                .bringIntoViewRequester(bring)
                .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
                .testTag("$tag.$index")
                .clip(RoundedCornerShape(Zapara.radii.control))
                .background(if (pressed) c.selection.compositeOver(restingColor) else restingColor)
                .controlFocusRing(focused, c.text1, Zapara.radii.control)
                .selectable(selected = active, role = Role.Tab, interactionSource = interactions,
                    indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                    onClick = { onSelect(index) })
                .padding(Zapara.space.s),
            contentAlignment = Alignment.Center
        ) {
            Text(
                label,
                style = if (active) Zapara.typography.bodyStrong else Zapara.typography.body,
                color = if (active) c.text1 else c.text2,
                softWrap = false
            )
        }
    }
    // #100 / AN-02: раньше ряд прокручивался вбок без признака, и при крупном шрифте «Послезавтра» уходило за край.
    // Теперь сегменты меряются по собственной ширине; если не влезают в одну строку — переносятся (FontFit.segmentRows).
    val minTouch = Zapara.space.minTouch
    Layout(
        modifier = container,
        content = { items.forEachIndexed { index, label -> segment(index, label, Modifier) } }
    ) { measurables, constraints ->
        val bounded = constraints.hasBoundedWidth && measurables.isNotEmpty()
        val available = if (bounded) constraints.maxWidth else Int.MAX_VALUE
        val minItem = if (bounded) (available / measurables.size).coerceAtLeast(minTouch.roundToPx()) else minTouch.roundToPx()
        val natural = measurables.map { maxOf(it.maxIntrinsicWidth(Constraints.Infinity), minItem) }
        val cells = if (bounded) FontFit.segmentRows(natural, available) else natural.map { it to 0 }
        val rowCount = (cells.maxOfOrNull { it.second } ?: -1) + 1
        val rowHeights = (0 until rowCount).map { r ->
            measurables.indices.filter { cells[it].second == r }.maxOf { measurables[it].minIntrinsicHeight(cells[it].first) }
        }
        val placeables = measurables.mapIndexed { i, m ->
            val (w, r) = cells[i]
            m.measure(Constraints.fixed(w, rowHeights[r]))
        }
        val width = if (bounded) available else placeables.sumOf { it.width }
        layout(width, rowHeights.sum()) {
            var y = 0
            (0 until rowCount).forEach { r ->
                var x = 0
                placeables.forEachIndexed { i, p -> if (cells[i].second == r) { p.placeRelative(x, y); x += p.width } }
                y += rowHeights[r]
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
fun ZBottomSheet(
    onDismiss: () -> Unit,
    tag: String,
    scrollable: Boolean = false,
    canDismiss: () -> Boolean = { true },
    footer: (@Composable ColumnScope.() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit
) {
    val c = Zapara.colors
    val sheetMotion = rememberSheetMotion(onDismiss, canDismiss)
    val entryOffset = with(LocalDensity.current) { 16.dp.toPx() }
    val dragDescription = stringResource(R.string.sheet_drag_down_to_close)
    val requestDismiss = { sheetMotion.requestDismiss() }
    Dialog(
        onDismissRequest = requestDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)
    ) {
    CompositionLocalProvider(LocalContentColor provides c.text1) {
    val window = (LocalView.current.parent as DialogWindowProvider).window
    SideEffect {
        window.setLayout(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT)
        window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_NOTHING)
    }
    BackHandler(onBack = requestDismiss)
    Box(Modifier.fillMaxSize().semantics { testTagsAsResourceId = true }) {
        Box(
            Modifier
                .fillMaxSize()
                .graphicsLayer { alpha = sheetMotion.visibility.value }
                .background(c.backdrop)
                .clickable(onClick = requestDismiss)
        )
        BoxWithConstraints(
            Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .systemBarsPadding()
                .imePadding()
        ) {
            val inlineFooter = scrollable && footer != null &&
                maxHeight < maxOf(320.dp, 210.dp * LocalDensity.current.fontScale)
            val footerContent: @Composable ColumnScope.() -> Unit = {
                footer?.let { actions ->
                    Spacer(Modifier.height(Zapara.space.m))
                    HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
                    Spacer(Modifier.height(Zapara.space.s))
                    actions()
                }
            }
            Column(
                Modifier
                    .fillMaxWidth()
                    .heightIn(max = maxHeight)
                    .graphicsLayer {
                        alpha = sheetMotion.visibility.value
                        translationY = (1f - sheetMotion.visibility.value) * entryOffset + sheetMotion.offset
                    }
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
                        .fillMaxWidth()
                        .height(48.dp)
                ) {
                    Box(
                        Modifier
                            .fillMaxSize()
                            .padding(horizontal = 48.dp)
                            .sheetDragHandle(sheetMotion)
                            .testTag("$tag.Handle")
                            .semantics { contentDescription = dragDescription },
                        contentAlignment = Alignment.Center
                    ) {
                        Box(
                            Modifier
                                .size(width = 28.dp, height = 3.dp)
                                .clip(RoundedCornerShape(Zapara.radii.pill))
                                .background(c.lineStrong)
                        )
                    }
                    ZIconButton(
                        R.drawable.ic_x,
                        stringResource(R.string.sheet_close_panel),
                        requestDismiss,
                        "$tag.Close",
                        modifier = Modifier.align(Alignment.CenterEnd).size(48.dp),
                        enabled = !sheetMotion.closing
                    )
                }
                Spacer(Modifier.height(Zapara.space.s))
                if (scrollable) {
                    Column(Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState())) {
                        content()
                        if (inlineFooter) footerContent()
                    }
                } else content()
                if (!inlineFooter) footerContent()
            }
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
        if (icon != null) {
            Box(Modifier.size(56.dp).clip(RoundedCornerShape(Zapara.radii.card)).background(c.chip),
                contentAlignment = Alignment.Center) {
                ZIcon(icon, null, Modifier.size(28.dp))
            }
            Spacer(Modifier.height(Zapara.space.m))
        }
        Text(title, style = Zapara.typography.section, color = c.text1, textAlign = TextAlign.Center,
            modifier = Modifier.widthIn(max = 400.dp))
        if (hint != null) {
            Spacer(Modifier.height(Zapara.space.xs))
            Text(hint, style = Zapara.typography.caption, color = c.text2, textAlign = TextAlign.Center,
                modifier = Modifier.widthIn(max = 400.dp))
        }
        if (actionText != null && onAction != null) {
            Spacer(Modifier.height(Zapara.space.m))
            ZButton(actionText, onAction, Modifier.widthIn(max = 320.dp).fillMaxWidth(), ghost = true)
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
