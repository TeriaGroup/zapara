package ru.bgtu_voenmeh.zapara.ui.settings

import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

data class SettingsUiState(
    val loaded: Boolean = false,
    val groupName: String = "",
    val groupUpdated: String = "",
    val stale: Boolean = false,
    val refreshing: Boolean = false,
    val theme: ThemeChoice = ThemeChoice.System,
    val animations: Boolean = true,
    val notifyEnabled: Boolean = true,
    val time1: String = "20:00",
    val time2: String = "07:30",
    val timeError: String? = null,
    val permissionMissing: Boolean = false,
    val exactAlarmMissing: Boolean = false,
    val selfUpdate: Boolean = true,
    val version: String = "",
    val autoUpdate: Boolean = true,
    val apiConfigured: Boolean = false,
    val useUniversityXml: Boolean = false
)

sealed interface SettingsEvent {
    data object ChangeGroup : SettingsEvent
    data object Refresh : SettingsEvent
    data class Theme(val index: Int) : SettingsEvent
    data class Animations(val enabled: Boolean) : SettingsEvent
    data class Notify(val enabled: Boolean) : SettingsEvent
    data class Time1(val value: String) : SettingsEvent
    data class Time2(val value: String) : SettingsEvent
    data object TestNotification : SettingsEvent
    data object OpenNotificationSettings : SettingsEvent
    data object OpenExactAlarmSettings : SettingsEvent
    data object OpenReleases : SettingsEvent
    data class AutoUpdate(val enabled: Boolean) : SettingsEvent
    data object CheckUpdate : SettingsEvent
    data object DownloadUpdate : SettingsEvent
    data object InstallUpdate : SettingsEvent
    data object CancelUpdate : SettingsEvent
    data class UseUniversityXml(val enabled: Boolean) : SettingsEvent
}
