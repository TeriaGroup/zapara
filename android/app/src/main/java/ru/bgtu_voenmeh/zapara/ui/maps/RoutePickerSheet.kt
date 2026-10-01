package ru.bgtu_voenmeh.zapara.ui.maps

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.HighlightText
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun RoutePickerSheet(state: RoutePickerUi, onEvent: (MapsEvent) -> Unit) {
    val c = Zapara.colors
    val title = stringResource(if (state.field == RouteField.From) R.string.maps_from else R.string.maps_to)
    val entrances = state.items.filter { it.kind == "entrance" }
    val rooms = state.items.filter { it.kind == "room" }
    val recentOutsideResults = if (state.query.isBlank())
        state.recent.filterNot { recent -> state.items.any { it.id == recent.id } }
    else emptyList()

    ZBottomSheet({ onEvent(MapsEvent.ClosePicker) }, "Sheet.Route") {
        // The entire picker is one lazy scroll region. At large font sizes and with the IME open,
        // fixed controls above a separate results list would consume the sheet viewport.
        LazyColumn(
            modifier = Modifier.fillMaxWidth().heightIn(min = 160.dp).weight(1f, fill = false)
                .testTag("Picker.List"),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)
        ) {
            item("title") { Text(title, style = Zapara.typography.section, color = c.text1) }
            item("fields") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZChip(stringResource(R.string.maps_from), selected = state.field == RouteField.From,
                        onClick = { onEvent(MapsEvent.SwitchPickerField(RouteField.From)) }, tag = "Picker.SelectFrom")
                    ZChip(stringResource(R.string.maps_to), selected = state.field == RouteField.To,
                        onClick = { onEvent(MapsEvent.SwitchPickerField(RouteField.To)) }, tag = "Picker.SelectTo")
                }
            }
            item("search") {
                OutlinedTextField(
                    value = state.query,
                    onValueChange = { onEvent(MapsEvent.QueryPlaces(it)) },
                    modifier = Modifier.fillMaxWidth().testTag("Picker.Search"),
                    placeholder = {
                        Text(stringResource(R.string.maps_places_search), style = Zapara.typography.caption, color = c.text3)
                    },
                    singleLine = true,
                    shape = RoundedCornerShape(Zapara.radii.control),
                    colors = OutlinedTextFieldDefaults.colors(
                        focusedContainerColor = c.chip,
                        unfocusedContainerColor = c.chip,
                        focusedBorderColor = c.lineStrong,
                        unfocusedBorderColor = c.chip,
                        focusedTextColor = c.text1,
                        unfocusedTextColor = c.text1
                    )
                )
            }
            item("current-floor") {
                ZButton(stringResource(R.string.ux100_platform_current_floor),
                    { onEvent(MapsEvent.PickerCurrentFloor) }, ghost = true, tag = "Picker.CurrentFloor")
            }
            if (state.query.isNotBlank()) item("search-scope") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZChip(stringResource(R.string.ux30_platform_search_everywhere), selected = state.searchCampus,
                        onClick = { onEvent(MapsEvent.SearchCampus(true)) }, tag = "Picker.SearchCampus")
                    ZChip(stringResource(R.string.ux30_platform_search_selected), selected = !state.searchCampus,
                        onClick = { onEvent(MapsEvent.SearchCampus(false)) }, tag = "Picker.SearchSelected")
                }
            }
            if (state.query.isBlank() || !state.searchCampus) item("location-filters") {
                FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    state.buildings.forEach { building ->
                        ZChip(building, selected = state.building == building,
                            onClick = { onEvent(MapsEvent.FilterPickerBuilding(
                                if (state.building == building) null else building)) },
                            tag = "Picker.Building.$building")
                    }
                    ZChip(stringResource(R.string.maps_places_all), selected = state.floor == null,
                        onClick = { onEvent(MapsEvent.FilterPickerFloor(null)) }, tag = "Picker.Floor.All")
                    state.floors.forEach { n ->
                        ZChip("$n", selected = state.floor == n,
                            onClick = { onEvent(MapsEvent.FilterPickerFloor(n)) }, tag = "Picker.Floor.$n")
                    }
                }
            }
            item("count") {
                Text(stringResource(R.string.ux30_platform_places_count, state.items.size),
                    style = Zapara.typography.caption, color = c.text2,
                    modifier = Modifier.testTag("Picker.ResultCount"))
            }
            if (state.items.isNotEmpty() &&
                (state.query.isNotBlank() || state.building != null || state.floor != null)) item("reset-filters") {
                ZButton(stringResource(R.string.ux60_picker_reset_filters),
                    { onEvent(MapsEvent.ResetPickerFilters) }, ghost = true, tag = "Picker.ResetFilters")
            }
            state.error?.let { error -> item("error") {
                Text(error, style = Zapara.typography.caption, color = c.bad)
            } }
            if ((state.field == RouteField.From && state.selectedFrom) ||
                (state.field == RouteField.To && state.selectedTo)) item("clear-selected") {
                ZButton(stringResource(R.string.ux100_platform_clear_selected),
                    { onEvent(MapsEvent.ClearEndpoint(state.field)) }, ghost = true, tag = "Picker.ClearSelected")
            }
            if (state.query.isBlank() && state.recent.isNotEmpty()) {
                if (recentOutsideResults.isNotEmpty()) item("recent-title") {
                    Text(stringResource(R.string.ux100_platform_recent_places),
                        style = Zapara.typography.caption, color = c.text2,
                        modifier = Modifier.testTag("Picker.RecentTitle"))
                }
                item("clear-recent") {
                    ZButton(stringResource(R.string.ux100_platform_clear_recent),
                        { onEvent(MapsEvent.ClearRecentPlaces) }, ghost = true, quiet = true,
                        tag = "Picker.ClearRecent")
                }
                items(recentOutsideResults, key = { "recent:${it.id}" }) { place ->
                    PlaceRow(place, "", state.field, state.epoch, onEvent)
                }
            }
            if (state.items.isEmpty()) {
                item("empty-message") {
                    Text(stringResource(R.string.maps_places_empty), style = Zapara.typography.body, color = c.text2)
                }
                if (state.query.isNotBlank()) item("clear-query") {
                    ZButton(stringResource(R.string.group_search_clear),
                        { onEvent(MapsEvent.QueryPlaces("")) }, ghost = true, tag = "Picker.ClearQuery")
                }
                if (state.query.isNotBlank() || state.building != null || state.floor != null) item("empty-reset") {
                    ZButton(stringResource(R.string.ux60_picker_reset_filters),
                        { onEvent(MapsEvent.ResetPickerFilters) }, ghost = true, tag = "Picker.ResetFilters")
                }
            }
            if (entrances.isNotEmpty()) {
                item("entrances-title") {
                    Text(stringResource(R.string.maps_places_entrances), style = Zapara.typography.caption, color = c.text2)
                }
                items(entrances, key = { "entrance:${it.id}" }) { place ->
                    PlaceRow(place, state.query, state.field, state.epoch, onEvent)
                }
            }
            if (rooms.isNotEmpty()) {
                item("rooms-title") {
                    Text(stringResource(R.string.maps_places_rooms), style = Zapara.typography.caption, color = c.text2)
                }
                items(rooms, key = { "room:${it.id}" }) { place ->
                    PlaceRow(place, state.query, state.field, state.epoch, onEvent)
                }
            }
        }
    }
}

@Composable
private fun PlaceRow(place: RoutePlaceUi, query: String, field: RouteField, epoch: Long,
    onEvent: (MapsEvent) -> Unit) {
    val c = Zapara.colors
    ZCard(
        Modifier.fillMaxWidth().testTag("Picker.Row.${place.id}")
            .clickable { onEvent(MapsEvent.PickPlace(place.id, field, epoch)) }
    ) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            HighlightText(place.label, query, Zapara.typography.bodyStrong, Modifier.weight(1f))
            Text(place.hint, style = Zapara.typography.caption, color = c.text2)
        }
    }
}
