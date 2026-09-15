package ru.bgtu_voenmeh.zapara

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import java.io.File

class DistFlavorTest {
    private val app = File(".").canonicalFile
    private val strings = File(app, "src/main/res/values/strings.xml").readText()
    private val section = File(app, "src/main/java/ru/bgtu_voenmeh/zapara/ui/settings/SettingsSection.kt").readText()
    private val settingsVm = File(app, "src/main/java/ru/bgtu_voenmeh/zapara/ui/settings/SettingsViewModel.kt").readText()
    private val shell = File(app, "src/main/java/ru/bgtu_voenmeh/zapara/ui/shell/ShellViewModel.kt").readText()

    @Test fun gradle_splits_github_self_update_from_rustore_store_build() {
        val gradle = File(app, "build.gradle.kts").readText()
        val github = gradle.substringAfter("create(\"github\")").substringBefore("create(\"rustore\")")
        val rustore = gradle.substringAfter("create(\"rustore\")").substringBefore("compileOptions")
        assertTrue(github.contains("SELF_UPDATE\", \"true\""))
        assertTrue(rustore.contains("SELF_UPDATE\", \"false\""))
        val rustoreManifest = File(app, "src/rustore/AndroidManifest.xml").readText()
        assertTrue(rustoreManifest.contains("REQUEST_INSTALL_PACKAGES"))
        assertTrue(rustoreManifest.contains("tools:node=\"remove\""))
    }

    @Test fun rustore_settings_explain_github_early_releases_without_in_app_update() {
        assertTrue(section.contains("if (state.selfUpdate) UpdatesCard"))
        assertTrue(section.contains("else RustoreUpdatesCard()"))
        val rustoreUi = section.substringAfter("fun RustoreUpdatesCard").substringBefore("fun UpdatesCard")
        assertFalse(rustoreUi.contains("Settings.UpdCheck"))
        assertFalse(rustoreUi.contains("Settings.UpdDownload"))
        assertFalse(rustoreUi.contains("Settings.UpdInstall"))
        assertTrue(rustoreUi.contains("settings_rustore"))
        assertTrue(rustoreUi.contains("settings_rustore_early"))
        assertTrue(rustoreUi.contains("settings_rustore_github"))
        assertTrue(rustoreUi.contains("Settings.RustoreGithub"))
        assertTrue(rustoreUi.contains("AutoUpdate.RELEASES_PAGE"))
        assertTrue(strings.contains("name=\"settings_rustore_early\""))
        assertTrue(strings.contains("name=\"settings_rustore_github\""))
        assertTrue(stringValue("settings_rustore").contains("RuStore"))
        val early = stringValue("settings_rustore_early").lowercase()
        assertTrue(early.contains("github"))
        assertTrue(early.contains("ранн"))
        assertEqualsReleasePage()
    }

    @Test fun rustore_never_starts_github_update_check() {
        assertTrue(shell.contains("BuildConfig.SELF_UPDATE"))
        assertTrue(shell.contains("checkOnStart()"))
        assertTrue(settingsVm.contains("BuildConfig.SELF_UPDATE"))
        val check = settingsVm.substringAfter("SettingsEvent.CheckUpdate")
            .substringBefore("SettingsEvent.DownloadUpdate")
        assertTrue(check.contains("SELF_UPDATE"))
    }

    @Test fun installed_flavor_matches_self_update_flag() {
        when (BuildConfig.FLAVOR) {
            "github" -> assertTrue(BuildConfig.SELF_UPDATE)
            "rustore" -> assertFalse(BuildConfig.SELF_UPDATE)
            else -> error("unexpected flavor ${BuildConfig.FLAVOR}")
        }
    }

    private fun stringValue(name: String): String {
        val match = Regex("""<string name="$name">([^<]+)</string>""").find(strings)
        return requireNotNull(match).groupValues[1]
    }

    private fun assertEqualsReleasePage() {
        assertTrue(AutoUpdate.RELEASES_PAGE.startsWith("https://github.com/TeriaGroup/zapara/releases"))
    }
}
