package ru.bgtu_voenmeh.zapara.ui.theme

import androidx.annotation.DrawableRes
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import ru.bgtu_voenmeh.zapara.R

/** A secondary navigation or utility action aligned with the content beside it. */
@Composable
fun ZActionButton(text: String, onClick: () -> Unit, modifier: Modifier = Modifier,
    enabled: Boolean = true, tag: String? = null, @DrawableRes leadingIcon: Int? = null) {
    ZButton(text, onClick, modifier.fillMaxWidth(), enabled = enabled, ghost = true, tag = tag,
        leadingIcon = leadingIcon, startAligned = true, trailingIcon = R.drawable.ic_chevron_right)
}

/** An expandable section keeps its state available to sighted and screen reader users. */
@Composable
fun ZDisclosureButton(text: String, expanded: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier,
    enabled: Boolean = true, tag: String? = null, @DrawableRes leadingIcon: Int? = null) {
    val description = stringResource(if (expanded) R.string.other_homework_group_expanded
        else R.string.other_homework_group_collapsed)
    ZButton(text, onClick, modifier.fillMaxWidth().semantics { stateDescription = description },
        enabled = enabled, ghost = true, tag = tag, leadingIcon = leadingIcon, startAligned = true,
        trailingIcon = R.drawable.ic_chevron_right, trailingIconRotation = if (expanded) 90f else 0f)
}
