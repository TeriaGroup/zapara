package ru.bgtu_voenmeh.zapara.ui.schedule

import android.app.DatePickerDialog
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.Alignment
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.rememberUiText
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale

@Composable
fun DateStrip(selected: LocalDate, today: LocalDate, pages: Map<LocalDate, DayPage> = emptyMap(), visibleCount: Int = 5, onQuickDay: ((Int)->Unit)? = null, onPick: (LocalDate) -> Unit) {
    val uiText = rememberUiText()
    val context = LocalContext.current
    var calendarOpen by remember { mutableStateOf(false) }
    val largeText = LocalDensity.current.fontScale >= 1.5f
    val labels = listOf(uiText(R.string.space_day_1), uiText(R.string.space_day_2), uiText(R.string.space_day_3))
    Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l).padding(bottom = Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        val quickDay = (0..2).firstOrNull { today.plusDays(it.toLong()) == selected } ?: -1
        val pickQuickDay: (Int) -> Unit = { offset -> onQuickDay?.invoke(offset) ?: onPick(today.plusDays(offset.toLong())) }
        ZSegmented(labels, quickDay, pickQuickDay, "Schedule.QuickDays")
        ZCard(Modifier.fillMaxWidth(), padded = false) {
        Column(Modifier.padding(Zapara.space.s), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            ZIconButton(R.drawable.ic_chevron_left, uiText(R.string.space_day_4), { onPick(selected.minusDays(1)) }, "Schedule.Previous")
            ZButton(selected.format(DateTimeFormatter.ofPattern("d MMMM yyyy", Locale("ru"))), {
                calendarOpen = true
                DatePickerDialog(context, { _, year, month, day -> onPick(LocalDate.of(year, month + 1, day)) },
                    selected.year, selected.monthValue - 1, selected.dayOfMonth).also { dialog -> dialog.setOnDismissListener { calendarOpen = false }; dialog.show() }
            }, modifier = Modifier.weight(1f), ghost = true, quiet = !calendarOpen, tag = "Schedule.Calendar", leadingIcon = R.drawable.ic_calendar)
            ZIconButton(R.drawable.ic_chevron_right, uiText(R.string.space_day_6), { onPick(selected.plusDays(1)) }, "Schedule.Next")
        }
        BoxWithConstraints(Modifier.fillMaxWidth()) {
        val scrollDates = largeText || maxWidth < 56.dp * visibleCount + Zapara.space.xs * (visibleCount - 1)
        Row(Modifier.fillMaxWidth().then(if (scrollDates) Modifier.horizontalScroll(rememberScrollState()) else Modifier), horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            (0L until visibleCount.toLong()).forEach { offset ->
                val date = selected.plusDays(offset)
                val page = pages[date]
                val count = when { page == null || page.dataState != null -> "—"; page.lessons.isEmpty() -> uiText(R.string.space_day_8); else -> pluralStringResource(R.plurals.schedule_pair_count, page.lessons.size, page.lessons.size) }
                val active = date == selected
                val ink = if (active) Zapara.colors.onAccent else Zapara.colors.text1
                val dateDescription = "${date.format(DateTimeFormatter.ofPattern("EEEE, d MMMM yyyy", Locale("ru")))} · ${if (count == "—") uiText(R.string.space_day_7) else count}"
                Column((if (scrollDates) Modifier.width(if (largeText) 96.dp else 72.dp) else Modifier.weight(1f)).sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
                    .semantics { contentDescription = dateDescription }
                    .testTag("Schedule.Date.$date").clip(RoundedCornerShape(Zapara.radii.control))
                    .background(if (active) Zapara.colors.accent else Zapara.colors.canvas)
                    .selectable(active, role = Role.Tab, onClick = { onPick(date) })
                    .padding(horizontal = Zapara.space.xs, vertical = Zapara.space.s),
                    horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
                    Text(date.format(DateTimeFormatter.ofPattern("EE", Locale("ru"))), style = Zapara.typography.caption, color = ink)
                    Text(date.dayOfMonth.toString(), style = Zapara.typography.section, color = ink)
                    Text(count, style = Zapara.typography.caption, color = if (active) ink else Zapara.colors.text2)
                }
            }
        }
        }
        }
        }
    }
}
