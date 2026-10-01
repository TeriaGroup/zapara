package ru.bgtu_voenmeh.zapara.ui.maps

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RecentPlacesUxTest {
    @Test fun recent_places_keep_latest_unique_choices_with_bounded_history() {
        val recent = RecentPlaceIds(3)
        listOf("a", "b", "c", "a", "d").forEach(recent::remember)
        assertEquals(listOf("d", "a", "c"), recent.items())
        recent.clear()
        assertTrue(recent.items().isEmpty())
    }

    @Test fun switching_picker_invalidates_old_choice_without_losing_search() {
        val place = RoutePlaceUi("room", "Аудитория", "", "room", "ГК", 1, "")
        val picker = RoutePickerUi(RouteField.From, "ауд", listOf(place), epoch = 7)
        val switched = picker.switchTo(RouteField.To, 8)
        assertEquals("ауд", switched.query)
        assertFalse(switched.accepts(MapsEvent.PickPlace("room", RouteField.From, 7)))
        assertTrue(switched.accepts(MapsEvent.PickPlace("room", RouteField.To, 8)))
    }

    @Test fun recent_choice_outside_active_filter_is_selectable_but_stale_or_unknown_choice_is_not() {
        val recent = RoutePlaceUi("gk-room", "Аудитория ГК", "", "room", "ГК", 4, "")
        val picker = RoutePickerUi(RouteField.To, "", items = emptyList(), epoch = 29,
            building = "УЛК", floor = 1, recent = listOf(recent))
        assertTrue(picker.accepts(MapsEvent.PickPlace("gk-room", RouteField.To, 29)))
        assertFalse(picker.accepts(MapsEvent.PickPlace("gk-room", RouteField.To, 28)))
        assertFalse(picker.accepts(MapsEvent.PickPlace("gk-room", RouteField.From, 29)))
        assertFalse(picker.accepts(MapsEvent.PickPlace("removed-from-catalog", RouteField.To, 29)))
        assertFalse(picker.copy(query = "другой поиск").accepts(MapsEvent.PickPlace("gk-room", RouteField.To, 29)))
    }
}
