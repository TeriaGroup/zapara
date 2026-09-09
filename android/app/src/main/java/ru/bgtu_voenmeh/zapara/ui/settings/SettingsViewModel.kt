package ru.bgtu_voenmeh.zapara.ui.settings

import android.app.AlarmManager
import android.content.Context
import android.os.Build
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.BuildConfig
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import ru.bgtu_voenmeh.zapara.data.Notifications
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

class SettingsViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(SettingsUiState())
    val state: StateFlow<SettingsUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: SettingsEvent) {
        when (event) {
            SettingsEvent.ChangeGroup -> { }
            SettingsEvent.Refresh -> refresh()
            is SettingsEvent.Theme -> save { it.copy(theme = ThemeChoice.entries[event.index.coerceIn(0, 2)].key) }
            is SettingsEvent.Animations -> save { it.copy(animations = event.enabled) }
            is SettingsEvent.Notify -> save { current ->
                current.copy(notifyEnabled = event.enabled).also {
                    if (event.enabled) Notifications.schedule(container.app) else Notifications.cancel(container.app)
                }
            }
            is SettingsEvent.Time1 -> {
                mutable.update { it.copy(time1 = event.value, timeError = SettingsLogic.validateTimes(event.value, it.time2, container.copy)) }
                persistTimes()
            }
            is SettingsEvent.Time2 -> {
                mutable.update { it.copy(time2 = event.value, timeError = SettingsLogic.validateTimes(it.time1, event.value, container.copy)) }
                persistTimes()
            }
            SettingsEvent.TestNotification -> viewModelScope.launch(Dispatchers.IO) {
                try {
                    Notifications.ensureChannel(container.app)
                    Notifications.showForTime(container.app, mutable.value.time1)
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaSettings", "test notification", e)
                }
            }
            SettingsEvent.OpenNotificationSettings, SettingsEvent.OpenExactAlarmSettings, SettingsEvent.OpenReleases -> { }
            is SettingsEvent.AutoUpdate -> {
                AutoUpdate.setAutoUpdateEnabled(container.app, event.enabled)
                container.update.setAuto(event.enabled)
                mutable.update { it.copy(autoUpdate = event.enabled) }
            }
            SettingsEvent.CheckUpdate -> container.update.check(manual = true)
            SettingsEvent.DownloadUpdate -> container.update.download()
            SettingsEvent.InstallUpdate -> container.update.install()
            SettingsEvent.CancelUpdate -> container.update.cancel()
            is SettingsEvent.UseUniversityXml -> {
                mutable.update { it.copy(useUniversityXml = event.enabled) }
                save { it.copy(useUniversityXml = event.enabled) }
            }
        }
    }

    private fun persistTimes() {
        val s = mutable.value
        if (s.timeError != null) return
        save { it.copy(notifyTime1 = s.time1, notifyTime2 = s.time2) }
        viewModelScope.launch(Dispatchers.IO) {
            try { Notifications.schedule(container.app) } catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "reschedule", e)
            }
        }
    }

    private fun refresh() {
        viewModelScope.launch {
            if (mutable.value.groupName.isEmpty()) {
                container.toasts.show(container.app.getString(R.string.pick_group_first), ToastKind.Bad)
                return@launch
            }
            mutable.update { it.copy(refreshing = true) }
            try {
                withContext(Dispatchers.IO) {
                    if (!container.timetable.pull()) {
                        throw IllegalStateException(container.api.lastError ?: container.app.getString(R.string.refresh_fail, ""))
                    }
                }
                container.events.emit(AppEvent.ScheduleChanged)
                container.toasts.show(container.app.getString(R.string.refresh_ok), ToastKind.Ok)
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "refresh", e)
                container.toasts.show(container.app.getString(R.string.refresh_fail, e.message ?: e.javaClass.simpleName), ToastKind.Bad)
            } finally {
                mutable.update { it.copy(refreshing = false) }
                reload()
            }
        }
    }

    private fun save(transform: (ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState) -> ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState) {
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) {
                    container.db.runInTransaction { container.repo.saveSettings(transform(container.repo.settings())) }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "save", e)
            }
        }
    }

    private suspend fun reload() {
        try {
            val now = container.clock()
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val groups = container.repo.groups()
                val name = groups.firstOrNull { it.id == prefs.myGroupId }?.name.orEmpty()
                SettingsUiState(
                    loaded = true, groupName = name,
                    groupUpdated = SettingsLogic.updatedLine(prefs.lastFetchedAt, now, container.copy),
                    stale = ShellLogic.isStale(prefs.lastFetchedAt, now),
                    refreshing = mutable.value.refreshing,
                    theme = ThemeChoice.fromKey(prefs.theme), animations = prefs.animations,
                    notifyEnabled = prefs.notifyEnabled,
                    time1 = prefs.notifyTime1 ?: "20:00", time2 = prefs.notifyTime2 ?: "07:30",
                    timeError = mutable.value.timeError,
                    permissionMissing = permissionMissing(),
                    exactAlarmMissing = !canExact(),
                    selfUpdate = BuildConfig.SELF_UPDATE,
                    version = BuildConfig.VERSION_NAME,
                    autoUpdate = AutoUpdate.isAutoUpdateEnabled(container.app),
                    apiConfigured = container.api.configured,
                    useUniversityXml = prefs.useUniversityXml
                )
            }
            mutable.value = snap
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSettings", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    private fun permissionMissing(): Boolean {
        if (Build.VERSION.SDK_INT < 33) return false
        return ContextCompat.checkSelfPermission(container.app, android.Manifest.permission.POST_NOTIFICATIONS) !=
            android.content.pm.PackageManager.PERMISSION_GRANTED
    }

    private fun canExact(): Boolean {
        val am = container.app.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        return Build.VERSION.SDK_INT < 31 || am.canScheduleExactAlarms()
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = SettingsViewModel(container) as T
        }
    }
}
