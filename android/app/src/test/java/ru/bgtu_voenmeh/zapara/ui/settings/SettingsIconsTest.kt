package ru.bgtu_voenmeh.zapara.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** #107 (AN-06): в настройках колокольчик и один пользователь вместо значка строки состояния «В». */
class SettingsIconsTest {
    private val settings = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/settings/SettingsSection.kt").readText()

    @Test fun settings_do_not_use_the_status_bar_icon() {
        assertFalse(settings.contains("R.drawable.ic_notification"))
        assertEquals(4, Regex("R\\.drawable\\.ic_bell").findAll(settings).count())
        assertTrue(settings.contains("SettingsOverviewRow(R.drawable.ic_bell, stringResource(R.string.settings_notify)"))
        assertTrue(settings.contains("SettingsOverviewRow(R.drawable.ic_user, stringResource(R.string.account_title)"))
        assertFalse("аккаунт — один пользователь, не группа", settings.contains("R.drawable.ic_users"))
    }

    @Test fun status_bar_icon_stays_only_in_system_notifications() {
        val users = File("src/main/java").walkTopDown().filter { it.extension == "kt" && it.readText().contains("R.drawable.ic_notification") }
            .map { it.name }.toList()
        assertEquals(listOf("Notifications.kt"), users)
    }

    @Test fun new_icons_match_the_lucide_style() {
        listOf("ic_bell", "ic_user").forEach { name ->
            val xml = File("src/main/res/drawable/$name.xml").readText()
            assertTrue(name, xml.contains("android:width=\"24dp\"") && xml.contains("android:viewportWidth=\"24\""))
            assertTrue(name, xml.contains("android:strokeWidth=\"1.75\"") && xml.contains("android:fillColor=\"#00000000\""))
        }
    }
}
