package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository

class MapsAlphaTest {
    @Test fun stock_maps_state_hides_routing() {
        assertFalse(MapsUiState().alphaMaps)
        assertFalse(ScheduleRepository.SettingsState().mapsAlpha)
        val dropped = MapsUiState(
            alphaMaps = true, showStack = true, stepsOpen = true,
            fromLabel = "Вход", toLabel = "493", routeLoading = true
        ).withoutRouting()
        assertFalse(dropped.alphaMaps)
        assertFalse(dropped.showStack)
        assertFalse(dropped.stepsOpen)
        assertEquals("", dropped.fromLabel)
        assertEquals("", dropped.toLabel)
        assertFalse(dropped.routeLoading)
    }

    @Test fun settings_and_maps_source_gate_routing_chrome_behind_alpha() {
        val settings = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/settings/SettingsSection.kt").readText()
        assertTrue(settings.contains("Settings.MapsAlpha"))
        assertTrue(settings.contains("Settings.MapsAlphaBadge"))
        assertTrue(settings.contains("settings_maps_alpha"))
        assertTrue(settings.contains("settings_maps_routes"))
        val maps = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsSection.kt").readText()
        assertTrue(maps.contains("state.alphaMaps"))
        val toNext = maps.indexOf("\"Maps.ToNext\"")
        val gate = maps.lastIndexOf("alphaMaps", toNext)
        assertTrue("ToNext must sit behind alphaMaps", gate >= 0 && toNext - gate < 400)
        assertTrue(maps.contains("if (state.alphaMaps)"))
        val floors = maps.substring(maps.indexOf("internal fun MapsFloorControls"))
        assertTrue(floors.contains("state.alphaMaps"))
        val strings = File("src/main/res/values/strings.xml").readText()
        assertTrue(Regex("""<string name="settings_maps_alpha">Альфа</string>""").containsMatchIn(strings))
        assertTrue(strings.contains("name=\"settings_maps_alpha_hint\""))
        val vm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/settings/SettingsViewModel.kt").readText()
        assertTrue(vm.contains("SettingsEvent.MapsAlpha"))
        val mapsVm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/maps/MapsViewModel.kt").readText()
        assertTrue(mapsVm.contains("mapsAlpha"))
        assertTrue(mapsVm.contains("MapsEvent.Browse"))
        val shell = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/shell/ZaparaApp.kt").readText()
        assertTrue(shell.contains("MapsEvent.Browse"))
        val db = File("src/main/java/ru/bgtu_voenmeh/zapara/data/db/ZaparaDatabase.kt").readText()
        assertTrue(db.contains("MIGRATION_5_6"))
        assertTrue(db.contains("version = 6"))
        assertTrue(db.contains("mapsAlpha"))
    }
}
