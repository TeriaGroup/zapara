package ru.bgtu_voenmeh.zapara.ui.schedule

import android.app.DatePickerDialog
import androidx.compose.foundation.layout.*
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
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import ru.bgtu_voenmeh.zapara.ui.components.ZSegmented
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
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
    val labels = listOf(uiText(R.string.space_day_1), uiText(R.string.space_day_2), uiText(R.string.space_day_3))
    Column(Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
        ZSegmented(labels, (0..2).firstOrNull { today.plusDays(it.toLong()) == selected } ?: -1,
            { offset -> onQuickDay?.invoke(offset) ?: onPick(LocalDate.now().plusDays(offset.toLong())) }, "Schedule.QuickDays")
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            ZIconButton(R.drawable.ic_chevron_left, uiText(R.string.space_day_4), { onPick(selected.minusDays(1)) }, "Schedule.Previous")
            ZButton(uiText(R.string.space_day_5), {
                calendarOpen = true
                DatePickerDialog(context, { _, year, month, day -> onPick(LocalDate.of(year, month + 1, day)) },
                    selected.year, selected.monthValue - 1, selected.dayOfMonth).also { dialog -> dialog.setOnDismissListener { calendarOpen = false }; dialog.show() }
            }, ghost = !calendarOpen, quiet = true)
            ZIconButton(R.drawable.ic_chevron_right, uiText(R.string.space_day_6), { onPick(selected.plusDays(1)) }, "Schedule.Next")
        }
        Text(selected.format(DateTimeFormatter.ofPattern("EEEE, d MMMM yyyy", Locale("ru"))), style = Zapara.typography.section)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            (0L until visibleCount.toLong()).forEach { offset ->
                val date = selected.plusDays(offset)
                val page = pages[date]
                val count = when { page == null || page.dataState != null -> uiText(R.string.space_day_7); page.lessons.isEmpty() -> uiText(R.string.space_day_8); else -> uiText(R.string.space_day_9, (page.lessons.size).toString()) }
                ZButton("${date.format(DateTimeFormatter.ofPattern("EE d", Locale("ru")))}\n$count", { onPick(date) },
                    modifier = Modifier.weight(1f).semantics { this.selected = date == selected }, ghost = date != selected, tag = "Schedule.Date.$date")
            }
        }
    }
}
