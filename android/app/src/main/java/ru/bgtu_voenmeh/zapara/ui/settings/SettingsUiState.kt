package ru.bgtu_voenmeh.zapara.ui.settings

import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

data class SupportThreadUi(val id: String, val subject: String, val messageCount: Int)
data class PendingSyncUi(val id: String, val type: String, val value: ru.bgtu_voenmeh.zapara.data.sync.SyncValue?,
    val deleted: Boolean, val conflict: Boolean)

data class SettingsUiState(
    val loaded: Boolean = false,
    val subgroupImpact: SubgroupImpact? = null,
    val subgroupImpactLoading: Boolean = false,
    val subgroupStreams: List<ru.bgtu_voenmeh.zapara.data.Subgroups.Stream> = emptyList(),
    val subgroupChoices: Map<String, String> = emptyMap(),
    val groupName: String = "",
    val groupId: String = "",
    val profileName: String = "",
    val undoSubgroup: ru.bgtu_voenmeh.zapara.ui.schedule.SubgroupUndoUi? = null,
    val groupUpdated: String = "",
    val stale: Boolean = false,
    val refreshing: Boolean = false,
    val theme: ThemeChoice = ThemeChoice.System,
    val animations: Boolean = true,
    val parityInvert: Boolean = false,
    val previewEvening: String = "",
    val previewMorning: String = "",
    val notifyEnabled: Boolean = true,
    val time1: String = "20:00",
    val time2: String = "07:30",
    val savedTime1: String = "20:00",
    val savedTime2: String = "07:30",
    val timeDirty: Boolean = false,
    val timeSaving: Boolean = false,
    val timeSaveError: String? = null,
    val preferencePending: Set<String> = emptySet(),
    val preferenceErrors: Map<String, String> = emptyMap(),
    val timeError: String? = null,
    val permissionMissing: Boolean = false,
    val exactAlarmMissing: Boolean = false,
    val selfUpdate: Boolean = true,
    val version: String = "",
    val autoUpdate: Boolean = true,
    val apiConfigured: Boolean = false,
    val useUniversityXml: Boolean = false,
    val mapsAlpha: Boolean = false,
    val syncConflicts: List<ru.bgtu_voenmeh.zapara.data.sync.SyncConflict> = emptyList(),
    val pendingSync: List<PendingSyncUi> = emptyList(),
    val syncBusy: Boolean = false,
    val syncError: String? = null,
    val signedIn: Boolean = false,
    val reportNote: String = "",
    val reportThread: List<ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note> = emptyList(),
    val supportThreads: List<SupportThreadUi> = emptyList(),
    val selectedSupportThreadId: String? = null,
    val supportLoading: Boolean = false,
    val supportLoaded: Boolean = false,
    val supportError: String? = null,
    val reportSending: Boolean = false,
    val reportSuccessVersion: Long = 0,
    val reportSuccessKey: String? = null,
    val reportSuccessDraftRevision: Long? = null,
    val cloudSync: ru.bgtu_voenmeh.zapara.data.sync.CloudSyncStatus = ru.bgtu_voenmeh.zapara.data.sync.CloudSyncStatus()
)

sealed interface SettingsEvent {
    data class PreviewSubgroup(val choice: Subgroup, val date: java.time.LocalDate? = null) : SettingsEvent
    data object ConfirmSubgroupImpact : SettingsEvent
    data object CloseSubgroupImpact : SettingsEvent
    data object RefreshPendingSync : SettingsEvent
    data class Subgroup(val streamId: String, val optionId: String, val groupId: String? = null,
        val profileName: String? = null) : SettingsEvent
    data object UndoSubgroup : SettingsEvent
    data class Invert(val on: Boolean) : SettingsEvent
    data object SyncNow : SettingsEvent
    data class RetryPreference(val key: String) : SettingsEvent
    data object ChangeGroup : SettingsEvent
    data object Refresh : SettingsEvent
    data class Theme(val index: Int) : SettingsEvent
    data class Animations(val enabled: Boolean) : SettingsEvent
    data class Notify(val enabled: Boolean) : SettingsEvent
    data class Time1(val value: String) : SettingsEvent
    data class Time2(val value: String) : SettingsEvent
    data object SaveTimes : SettingsEvent
    data object CancelTimes : SettingsEvent
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
    data class MapsAlpha(val enabled: Boolean) : SettingsEvent
    data class ResolveSync(val conflict: ru.bgtu_voenmeh.zapara.data.sync.SyncConflict, val keepLocal: Boolean) : SettingsEvent
    data class Report(val subject: String, val body: String, val photos: List<Pair<String, ByteArray>> = emptyList(),
        val draftRevision: Long? = null) : SettingsEvent
    data object RetrySupport : SettingsEvent
    data class SelectSupportThread(val id: String?) : SettingsEvent
}
