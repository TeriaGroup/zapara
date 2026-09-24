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
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
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
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetUpdater

class SettingsViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(SettingsUiState())
    val state: StateFlow<SettingsUiState> = mutable.asStateFlow()
    private val saves = Mutex()
    private var reloadTicket = 0

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: SettingsEvent) {
        when (event) {
            is SettingsEvent.ResolveSync -> resolveSync(event)
            SettingsEvent.ChangeGroup -> { }
            SettingsEvent.Refresh -> refresh()
            is SettingsEvent.Theme -> {
                val choice = ThemeChoice.entries[event.index.coerceIn(0, 2)]
                mutable.update { it.copy(theme = choice) }
                save(transform = { it.copy(theme = choice.key) })
            }
            is SettingsEvent.Animations -> {
                mutable.update { it.copy(animations = event.enabled) }
                val motionChange = WidgetUpdater.animationsChanging(container)
                save(transform = { it.copy(animations = event.enabled) },
                    finished = { WidgetUpdater.animationsSaved(container, motionChange) })
            }
            is SettingsEvent.Notify -> {
                mutable.update { it.copy(notifyEnabled = event.enabled) }
                save(transform = { it.copy(notifyEnabled = event.enabled) }, after = { saved ->
                    viewModelScope.launch(Dispatchers.IO) {
                        try {
                            if (saved.notifyEnabled) Notifications.schedule(container.app)
                            else Notifications.cancel(container.app)
                        } catch (e: Exception) {
                            android.util.Log.w("ZaparaSettings", "notify", e)
                        }
                    }
                })
            }
            is SettingsEvent.Time1 -> {
                mutable.update { it.copy(time1 = event.value, timeError = SettingsLogic.validateTimes(event.value, it.time2, container.copy)) }
                persistTimes()
            }
            is SettingsEvent.Time2 -> {
                mutable.update { it.copy(time2 = event.value, timeError = SettingsLogic.validateTimes(it.time1, event.value, container.copy)) }
                persistTimes()
            }
            SettingsEvent.TestNotification -> viewModelScope.launch {
                try {
                    if (permissionMissing()) {
                        container.toasts.show(container.app.getString(R.string.settings_perm_notify), ToastKind.Bad)
                        return@launch
                    }
                    withContext(Dispatchers.IO) {
                        Notifications.ensureChannel(container.app)
                        Notifications.showForTime(container.app, mutable.value.time1)
                    }
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaSettings", "test notification", e)
                }
            }
            SettingsEvent.OpenNotificationSettings, SettingsEvent.OpenExactAlarmSettings, SettingsEvent.OpenReleases -> { }
            is SettingsEvent.AutoUpdate -> if (BuildConfig.SELF_UPDATE) {
                AutoUpdate.setAutoUpdateEnabled(container.app, event.enabled)
                container.update.setAuto(event.enabled)
                mutable.update { it.copy(autoUpdate = event.enabled) }
            }
            SettingsEvent.CheckUpdate -> if (BuildConfig.SELF_UPDATE) container.update.check(manual = true)
            SettingsEvent.DownloadUpdate -> if (BuildConfig.SELF_UPDATE) container.update.download()
            SettingsEvent.InstallUpdate -> if (BuildConfig.SELF_UPDATE) container.update.install()
            SettingsEvent.CancelUpdate -> if (BuildConfig.SELF_UPDATE) container.update.cancel()
            is SettingsEvent.UseUniversityXml -> {
                mutable.update { it.copy(useUniversityXml = event.enabled) }
                save(transform = { it.copy(useUniversityXml = event.enabled) })
            }
            is SettingsEvent.Report -> report(event.subject, event.body, event.photos, event.logs)
            is SettingsEvent.MapsAlpha -> {
                mutable.update { it.copy(mapsAlpha = event.enabled) }
                save(transform = { it.copy(mapsAlpha = event.enabled) }, after = {
                    container.events.emit(AppEvent.PersonalizationChanged)
                })
            }
        }
    }

    private fun resolveSync(event: SettingsEvent.ResolveSync) {
        if (mutable.value.syncBusy) return
        mutable.update { it.copy(syncBusy = true, syncError = null) }
        viewModelScope.launch {
            try {
                val changed = withContext(Dispatchers.IO) { container.privateSync?.resolve(event.conflict, event.keepLocal) == true }
                if (!changed) mutable.update { it.copy(syncError = container.app.getString(R.string.sync_choice_changed)) }
                reload()
                if (changed) viewModelScope.launch(Dispatchers.IO) {
                    try { container.privateSync?.sync() }
                    catch (e: CancellationException) { throw e }
                    catch (e: Exception) { android.util.Log.w("ZaparaSync", "sync after choice", e) }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                mutable.update { it.copy(syncError = container.app.getString(R.string.sync_choice_failed)) }
            } finally {
                mutable.update { it.copy(syncBusy = false) }
            }
        }
    }

    private fun persistTimes() {
        val s = mutable.value
        if (s.timeError != null) return
        save(transform = { it.copy(notifyTime1 = s.time1, notifyTime2 = s.time2) }, after = {
            viewModelScope.launch(Dispatchers.IO) {
                try { Notifications.schedule(container.app) } catch (e: Exception) {
                    android.util.Log.w("ZaparaSettings", "reschedule", e)
                }
            }
        })
    }

    private fun refresh() {
        if (mutable.value.refreshing) return
        if (mutable.value.groupName.isEmpty()) {
            container.toasts.show(container.app.getString(R.string.pick_group_first), ToastKind.Bad)
            return
        }
        mutable.update { it.copy(refreshing = true) }
        viewModelScope.launch {
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

    private fun save(
        transform: (ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState) -> ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState,
        after: (ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState) -> Unit = {},
        finished: () -> Unit = {}
    ) {
        viewModelScope.launch {
            try {
                val saved = saves.withLock {
                    withContext(Dispatchers.IO) {
                        var next: ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState? = null
                        container.db.runInTransaction {
                            next = transform(container.repo.settings())
                            container.repo.saveSettings(next!!)
                        }
                        next!!
                    }
                }
                after(saved)
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "save", e)
            } finally { finished() }
        }
    }

    private suspend fun reload() {
        val ticket = ++reloadTicket
        try {
            val now = container.clock()
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val groups = container.repo.groups()
                val name = groups.firstOrNull { it.id == prefs.myGroupId }?.name.orEmpty()
                SettingsUiState(
                    loaded = true, groupName = name,
                    groupUpdated = SettingsLogic.updatedLine(
                        prefs.lastFetchedAt, now, container.copy,
                        hasLocal = !prefs.myGroupId.isNullOrEmpty() &&
                            container.repo.allForGroup(prefs.myGroupId!!).isNotEmpty()
                    ),
                    stale = ShellLogic.isStale(prefs.lastFetchedAt, now),
                    refreshing = mutable.value.refreshing,
                    theme = ThemeChoice.fromKey(prefs.theme), animations = prefs.animations,
                    notifyEnabled = prefs.notifyEnabled,
                    time1 = prefs.notifyTime1 ?: "20:00", time2 = prefs.notifyTime2 ?: "07:30",
                    timeError = null,
                    permissionMissing = permissionMissing(),
                    exactAlarmMissing = !canExact(),
                    selfUpdate = BuildConfig.SELF_UPDATE,
                    version = BuildConfig.VERSION_NAME,
                    autoUpdate = AutoUpdate.isAutoUpdateEnabled(container.app),
                    apiConfigured = container.api.configured,
                    useUniversityXml = prefs.useUniversityXml,
                    mapsAlpha = prefs.mapsAlpha,
                    syncConflicts = if (container.profile.isGuest) emptyList() else container.outbox.inbox.conflicts(),
                    syncBusy = false,
                    syncError = null,
                    signedIn = !container.profile.isGuest,
                    reportNote = mutable.value.reportNote,
                    reportThread = mutable.value.reportThread
                )
            }
            if (ticket != reloadTicket) return
            val thread = loadSupport(snap.signedIn)
            if (ticket != reloadTicket) return
            mutable.update { cur ->
                val editingTimes = cur.timeError != null
                snap.copy(
                    refreshing = cur.refreshing,
                    syncBusy = cur.syncBusy,
                    syncError = cur.syncError,
                    time1 = if (editingTimes) cur.time1 else snap.time1,
                    time2 = if (editingTimes) cur.time2 else snap.time2,
                    timeError = if (editingTimes) cur.timeError else snap.timeError,
                    reportNote = cur.reportNote,
                    reportThread = if (thread.isEmpty()) cur.reportThread else thread
                )
            }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSettings", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    private var reportThreadId: String? = null

    private suspend fun loadSupport(signedIn: Boolean): List<ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note> {
        if (!signedIn) return emptyList()
        val client = container.accounts ?: return emptyList()
        val token = container.accessToken() ?: return emptyList()
        return try {
            val opened = withContext(Dispatchers.IO) { client.supportThreads(token).lastOrNull() } ?: return emptyList()
            reportThreadId = opened.id
            opened.messages.map { ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note(it.author, supportText(it)) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSettings", "support", e)
            emptyList()
        }
    }

    private fun supportText(message: ru.bgtu_voenmeh.zapara.data.accounts.SupportMessage): String {
        val extra = message.attachments.joinToString("\n") { if (it.kind == "photo") "Фото: ${it.name}" else "Лог: ${it.name}" }
        return if (extra.isEmpty()) message.body else message.body + "\n" + extra
    }

    private fun report(subject: String, body: String, photos: List<Pair<String, ByteArray>>, logs: List<Pair<String, ByteArray>>) {
        val local = ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.submit(mutable.value.signedIn, mutable.value.reportThread, subject, body)
        if (local.error != null) {
            mutable.update { it.copy(reportNote = local.error) }
            return
        }
        val client = container.accounts
        viewModelScope.launch {
            val token = container.accessToken()
            if (client == null || token.isNullOrEmpty()) {
                mutable.update { it.copy(reportNote = "Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.") }
                return@launch
            }
            try {
                val saved = withContext(Dispatchers.IO) {
                    val existing = reportThreadId
                    if (existing == null) client.openSupport(token, subject.trim(), body.trim(), photos, logs)
                    else client.continueSupport(token, existing, body.trim(), photos, logs)
                }
                reportThreadId = saved.id
                mutable.update {
                    it.copy(
                        reportNote = "",
                        reportThread = saved.messages.map { line -> ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note(line.author, supportText(line)) }
                    )
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "support send", e)
                mutable.update { it.copy(reportNote = "Сообщение не отправилось") }
            }
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
