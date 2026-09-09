package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.theme.*

class ThemeSmokeTest {
    @Test fun dark_primitives() = showcase(ThemeChoice.Dark, 1f)
    @Test fun light_primitives() = showcase(ThemeChoice.Light, 1f)
    @Test fun dark_large_font() = showcase(ThemeChoice.Dark, 1.3f)
    @Test fun light_large_font() = showcase(ThemeChoice.Light, 1.3f)

    private fun showcase(choice: ThemeChoice, scale: Float) {
        // Same palette/type/geometry/presence/click assertions, using public platform input.
        ThemeNativeCaptureTest().capture(choice, scale)
    }
}

@Composable
internal fun Showcase(onSave: () -> Unit) {
    Column(Modifier.fillMaxSize().background(Zapara.colors.canvas).verticalScroll(rememberScrollState())
        .padding(Zapara.space.l), verticalArrangement = Arrangement.spacedBy(Zapara.space.l)) {
        Text(stringResource(R.string.theme_showcase), style = Zapara.typography.title, color = Zapara.colors.text1)
        ZCard(Modifier.fillMaxWidth()) {
            Text(stringResource(R.string.theme_sample_section), style = Zapara.typography.section)
            Text(stringResource(R.string.theme_sample_body), style = Zapara.typography.body)
            // Text2 on light Card falls below 4.5: use existing Text1 for meaningful caption.
            Text(stringResource(R.string.theme_sample_caption), style = Zapara.typography.caption, color = Zapara.colors.text1)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.theme_save), onSave, Modifier.testTag("Theme.Save"))
            ZButton(stringResource(R.string.theme_cancel), {}, ghost = true)
        }
        ZButton(stringResource(R.string.theme_disabled), {}, Modifier.testTag("Theme.Disabled"), enabled = false)
        Text(stringResource(R.string.theme_icons), style = Zapara.typography.section, color = Zapara.colors.text1)
        val icons = listOf(R.drawable.ic_calendar,R.drawable.ic_week,R.drawable.ic_summary,R.drawable.ic_teachers,
            R.drawable.ic_map,R.drawable.ic_friends,R.drawable.ic_homework,R.drawable.ic_settings,R.drawable.ic_sun,
            R.drawable.ic_moon,R.drawable.ic_chevron_left,R.drawable.ic_chevron_right,R.drawable.ic_pencil,
            R.drawable.ic_plus,R.drawable.ic_minus,R.drawable.ic_map_pin,R.drawable.ic_x,R.drawable.ic_check,
            R.drawable.ic_trash,R.drawable.ic_search,R.drawable.ic_refresh,R.drawable.ic_alert,R.drawable.ic_users,
            R.drawable.ic_download,R.drawable.ic_external_link,R.drawable.ic_fullscreen,R.drawable.ic_menu)
        icons.chunked(5).forEachIndexed { row, chunk ->
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                chunk.forEachIndexed { column, icon ->
                    Box(Modifier.size(Zapara.space.minTouch).testTag("Theme.Icon.${row*5+column}"), contentAlignment = androidx.compose.ui.Alignment.Center) {
                        ZIcon(icon, stringResource(R.string.theme_icon_index, row * 5 + column + 1))
                    }
                }
            }
        }
    }
}
