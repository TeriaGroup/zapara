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
import kotlinx.coroutines.delay
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
import ru.bgtu_voenmeh.zapara.data.SupportLogs
import ru.bgtu_voenmeh.zapara.data.accounts.SupportThread
import ru.bgtu_voenmeh.zapara.data.Subgroups
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic
import ru.bgtu_voenmeh.zapara.ui.schedule.SubgroupUndoUi
import ru.bgtu_voenmeh.zapara.ui.schedule.subgroupOptionExists
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetUpdater

class SettingsViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(SettingsUiState())
    val state: StateFlow<SettingsUiState> = mutable.asStateFlow()
    private val saves = Mutex()
    private var reloadTicket = 0
    private var syncActionBusy = false
    private val preferenceVersions = mutableMapOf<String, Long>()
    private val retryPreferences = mutableMapOf<String, Any>()
    private var subgroupUndoSerial = 0L
    private var subgroupPreviewTicket = 0L
    private val subgroupWrites = Mutex()
    private var supportLoadTicket = 0L
    private var supportData: List<SupportThread> = emptyList()
    private val supportAcknowledged = LinkedHashMap<String, SupportThread>()

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
        container.privateSync?.let { coordinator ->
            viewModelScope.launch { coordinator.status.collect { status ->
                mutable.update { it.copy(cloudSync = status, syncBusy = syncActionBusy || status.running) }
            } }
        }
    }

    fun onEvent(event: SettingsEvent) {
        when (event) {
            is SettingsEvent.PreviewSubgroup -> previewSubgroup(event.choice, event.date)
            SettingsEvent.ConfirmSubgroupImpact -> mutable.value.subgroupImpact?.let { approval ->
                subgroupPreviewTicket++
                mutable.update { it.copy(subgroupImpact = null) }; chooseSubgroup(approval.event, approval)
            }
            SettingsEvent.CloseSubgroupImpact -> { subgroupPreviewTicket++; mutable.update { it.copy(subgroupImpact = null, subgroupImpactLoading = false) } }
            SettingsEvent.RefreshPendingSync -> viewModelScope.launch { reload() }
            is SettingsEvent.Subgroup -> chooseSubgroup(event)
            SettingsEvent.UndoSubgroup -> undoSubgroup()
            is SettingsEvent.ResolveSync -> resolveSync(event)
            is SettingsEvent.RetryPreference -> retryPreference(event.key)
            is SettingsEvent.Invert -> {
                mutable.update { it.copy(parityInvert = event.on) }
                save("parity", event.on, { it.copy(parityInvert = event.on) },
                    after = { viewModelScope.launch { container.events.emit(AppEvent.ScheduleChanged) } })
            }
            SettingsEvent.SyncNow -> if (!container.profile.isGuest && !mutable.value.syncBusy) {
                syncActionBusy = true
                mutable.update { it.copy(syncBusy = true, syncError = null) }
                viewModelScope.launch {
                    try { withContext(Dispatchers.IO) { container.privateSync?.sync() }; reload() }
                    catch (e: CancellationException) { throw e }
                    catch (_: Exception) { mutable.update { it.copy(syncError = container.app.getString(R.string.ux60_sync_failed)) } }
                    finally { syncActionBusy = false; mutable.update { it.copy(syncBusy = it.cloudSync.running) } }
                }
            }
            SettingsEvent.ChangeGroup -> { }
            SettingsEvent.Refresh -> refresh()
            is SettingsEvent.Theme -> {
                val choice = ThemeChoice.entries[event.index.coerceIn(0, 2)]
                mutable.update { it.copy(theme = choice) }
                save("theme", choice, transform = { it.copy(theme = choice.key) })
            }
            is SettingsEvent.Animations -> {
                mutable.update { it.copy(animations = event.enabled) }
                val motionChange = WidgetUpdater.animationsChanging(container)
                save("animations", event.enabled, transform = { it.copy(animations = event.enabled) },
                    finished = { WidgetUpdater.animationsSaved(container, motionChange) })
            }
            is SettingsEvent.Notify -> {
                mutable.update { it.copy(notifyEnabled = event.enabled) }
                save("notify", event.enabled, transform = { it.copy(notifyEnabled = event.enabled) }, after = { saved ->
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
                mutable.update { SettingsLogic.editNotificationTime(it, true, event.value, container.copy) }
            }
            is SettingsEvent.Time2 -> {
                mutable.update { SettingsLogic.editNotificationTime(it, false, event.value, container.copy) }
            }
            SettingsEvent.SaveTimes -> persistTimes()
            SettingsEvent.CancelTimes -> mutable.update { if (it.timeSaving) it else SettingsLogic.cancelNotificationTimeDraft(it) }
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
                save("source", event.enabled, transform = { it.copy(useUniversityXml = event.enabled) })
            }
            is SettingsEvent.Report -> report(event.subject, event.body, event.photos, event.draftRevision)
            SettingsEvent.RetrySupport -> viewModelScope.launch { refreshSupport() }
            is SettingsEvent.SelectSupportThread -> selectSupportThread(event.id)
            is SettingsEvent.MapsAlpha -> {
                mutable.update { it.copy(mapsAlpha = event.enabled) }
                save("maps", event.enabled, transform = { it.copy(mapsAlpha = event.enabled) }, after = {
                    container.events.emit(AppEvent.PersonalizationChanged)
                })
            }
        }
    }

    private fun resolveSync(event: SettingsEvent.ResolveSync) {
        if (mutable.value.syncBusy) return
        syncActionBusy = true
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
                syncActionBusy = false
                mutable.update { it.copy(syncBusy = it.cloudSync.running) }
            }
        }
    }

    private fun persistTimes() {
        val s = mutable.value
        if (!s.timeDirty || s.timeSaving || s.timeError != null) return
        mutable.update { it.copy(timeSaving = true, timeSaveError = null) }
        viewModelScope.launch {
            try {
                val saved = saves.withLock { withContext(Dispatchers.IO) {
                    var next: ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState? = null
                    container.db.runInTransaction {
                        next = container.repo.settings().copy(notifyTime1 = s.time1.trim(), notifyTime2 = s.time2.trim())
                        container.repo.saveSettings(next!!)
                    }
                    next!!
                } }
                mutable.update { current -> current.copy(
                    savedTime1 = saved.notifyTime1 ?: s.time1.trim(),
                    savedTime2 = saved.notifyTime2 ?: s.time2.trim(),
                    timeDirty = current.time1 != (saved.notifyTime1 ?: s.time1.trim()) ||
                        current.time2 != (saved.notifyTime2 ?: s.time2.trim()),
                    timeSaving = false, timeSaveError = null
                ) }
                if (saved.notifyEnabled) withContext(Dispatchers.IO) {
                    try { Notifications.schedule(container.app) } catch (e: Exception) {
                        android.util.Log.w("ZaparaSettings", "reschedule", e)
                    }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "save notification times", e)
                mutable.update { it.copy(timeSaving = false,
                    timeSaveError = container.app.getString(R.string.ux30_notify_save_failed)) }
            }
        }
    }

    private fun previewSubgroup(event: SettingsEvent.Subgroup, date: java.time.LocalDate? = null) {
        val owner = container.profile.databaseName
        val group = mutable.value.groupId
        if (event.profileName != owner || event.groupId != group || group.isBlank()) return
        val ticket = ++subgroupPreviewTicket
        mutable.update { it.copy(subgroupImpact = null, subgroupImpactLoading = true) }
        viewModelScope.launch {
            try {
            val preview = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val rows = container.repo.allForGroup(group)
                if (prefs.myGroupId != group || !subgroupOptionExists(Subgroups.index(rows).streams, event.streamId, event.optionId)) null
                else subgroupImpact(event, ru.bgtu_voenmeh.zapara.data.SchedCtx(group, prefs.periodStart, prefs.weekCount, prefs.parityInvert),
                    container.subgroupChoices(group), rows, date ?: container.clock().toLocalDate(), container.copy)
            }
            if (ticket == subgroupPreviewTicket && owner == container.profile.databaseName && group == mutable.value.groupId)
                mutable.update { it.copy(subgroupImpact = preview) }
            } catch (cancel: CancellationException) { throw cancel }
            catch (_: Exception) {
                if (ticket == subgroupPreviewTicket) container.toasts.show(container.app.getString(R.string.uxnext_subgroup_failed), ToastKind.Bad)
            } finally { if (ticket == subgroupPreviewTicket) mutable.update { it.copy(subgroupImpactLoading = false) } }
        }
    }

    private fun chooseSubgroup(event: SettingsEvent.Subgroup, approval: SubgroupImpact? = null) {
        val group = event.groupId ?: mutable.value.groupId
        if (group.isBlank() || group != mutable.value.groupId) return
        val profile = container.profile.databaseName
        if (event.profileName != null && event.profileName != profile) return
        viewModelScope.launch {
            try {
                val change = subgroupWrites.withLock { withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != group ||
                        (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container?.profile
                            ?.let { it != container.profile } == true)
                        return@withContext null
                    val streams = Subgroups.index(container.repo.allForGroup(group)).streams
                    if (approval != null) {
                        val prefs = container.repo.settings()
                        val ctx = ru.bgtu_voenmeh.zapara.data.SchedCtx(group, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                        if (!approval.stillMatches(profile, ctx, container.subgroupChoices(group), container.repo.allForGroup(group))) {
                            container.toasts.show(container.app.getString(R.string.ux300_subgroup_stale), ToastKind.Bad)
                            return@withContext null
                        }
                    }
                    if (!subgroupOptionExists(streams, event.streamId, event.optionId)) return@withContext null
                    val (before, after) = container.subgroups.selectWithPrevious(profile, group, event.streamId, event.optionId)
                    SubgroupUndoUi(profile, group, event.streamId, before, after)
                } }
                if (change != null && mutable.value.groupId == group) {
                    val serial = ++subgroupUndoSerial
                    mutable.update { it.copy(undoSubgroup = change,
                        subgroupChoices = if (change.after == null) it.subgroupChoices - change.streamId
                            else it.subgroupChoices + (change.streamId to change.after)) }
                    container.events.emit(AppEvent.SubgroupChanged)
                    viewModelScope.launch { delay(5_000); if (serial == subgroupUndoSerial)
                        mutable.update { it.copy(undoSubgroup = null) } }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "subgroup", e)
                container.toasts.show(container.app.getString(R.string.uxnext_subgroup_failed), ToastKind.Bad)
            }
        }
    }

    private fun undoSubgroup() {
        val undo = mutable.value.undoSubgroup ?: return
        ++subgroupUndoSerial
        mutable.update { it.copy(undoSubgroup = null) }
        viewModelScope.launch {
            try {
                val restored = subgroupWrites.withLock { withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != undo.groupId ||
                        (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container?.profile
                            ?.let { it != container.profile } == true)
                        return@withContext false
                    val streams = Subgroups.index(container.repo.allForGroup(undo.groupId)).streams
                    val current = container.subgroups.read(undo.profile, undo.groupId)[undo.streamId]
                    undo.allows(container.profile.databaseName, undo.groupId, current, streams) &&
                        container.subgroups.restoreIfCurrent(undo.profile, undo.groupId, undo.streamId, undo.after, undo.before)
                } }
                if (restored) container.events.emit(AppEvent.SubgroupChanged)
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "subgroup undo", e)
                container.toasts.show(container.app.getString(R.string.uxnext_subgroup_failed), ToastKind.Bad)
            }
        }
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

    private fun retryPreference(key: String) {
        when (val value = retryPreferences[key]) {
            is Boolean -> when (key) {
                "parity" -> onEvent(SettingsEvent.Invert(value))
                "animations" -> onEvent(SettingsEvent.Animations(value))
                "notify" -> onEvent(SettingsEvent.Notify(value))
                "source" -> onEvent(SettingsEvent.UseUniversityXml(value))
                "maps" -> onEvent(SettingsEvent.MapsAlpha(value))
            }
            is ThemeChoice -> if (key == "theme") onEvent(SettingsEvent.Theme(value.ordinal))
        }
    }

    private fun save(
        key: String,
        desired: Any,
        transform: (ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState) -> ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState,
        after: (ru.bgtu_voenmeh.zapara.data.ScheduleRepository.SettingsState) -> Unit = {},
        finished: () -> Unit = {}
    ) {
        val version = preferenceVersions.getOrDefault(key, 0L) + 1
        preferenceVersions[key] = version
        retryPreferences[key] = desired
        mutable.update { it.copy(preferencePending = it.preferencePending + key,
            preferenceErrors = it.preferenceErrors - key) }
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
                if (preferenceVersions[key] == version) {
                    retryPreferences.remove(key)
                    mutable.update { current -> SettingsLogic.persistedPreference(current, key, saved)
                        .copy(preferencePending = current.preferencePending - key,
                            preferenceErrors = current.preferenceErrors - key) }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "save", e)
                val persisted = try { withContext(Dispatchers.IO) { container.repo.settings() } }
                    catch (_: Exception) { null }
                if (preferenceVersions[key] == version) mutable.update { current ->
                    (persisted?.let { SettingsLogic.persistedPreference(current, key, it) } ?: current).copy(
                        preferencePending = current.preferencePending - key,
                        preferenceErrors = current.preferenceErrors +
                            (key to container.app.getString(R.string.ux60_preference_save_failed)))
                }
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
                val gid = prefs.myGroupId.orEmpty()
                val own = container.ownLessons()
                fun preview(date: java.time.LocalDate): String = if (gid.isBlank()) container.app.getString(R.string.pick_group_first) else {
                    val lessons = ru.bgtu_voenmeh.zapara.data.Schedule.lessonsForDate(own, gid, date, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                    if (lessons.isEmpty()) container.app.getString(R.string.no_lessons_day)
                    else lessons.joinToString("\n") { "${it.timeStart} · ${ru.bgtu_voenmeh.zapara.ui.LessonFormat.stripType(it.subjectRaw, it.typeRaw)} · ${it.classroomRaw}" }
                }
                SettingsUiState(
                    loaded = true, groupName = name, groupId = gid,
                    profileName = container.profile.databaseName,
                    subgroupStreams = ru.bgtu_voenmeh.zapara.data.Subgroups.index(container.repo.allForGroup(prefs.myGroupId.orEmpty())).streams,
                    subgroupChoices = container.subgroupChoices(prefs.myGroupId.orEmpty()),
                    groupUpdated = SettingsLogic.updatedLine(
                        prefs.lastFetchedAt, now, container.copy,
                        hasLocal = !prefs.myGroupId.isNullOrEmpty() &&
                            container.repo.allForGroup(prefs.myGroupId!!).isNotEmpty()
                    ),
                    stale = ShellLogic.isStale(prefs.lastFetchedAt, now),
                    refreshing = mutable.value.refreshing,
                    theme = ThemeChoice.fromKey(prefs.theme), animations = prefs.animations,
                    parityInvert = prefs.parityInvert,
                    previewEvening = preview(now.toLocalDate().plusDays(1)), previewMorning = preview(now.toLocalDate()),
                    notifyEnabled = prefs.notifyEnabled,
                    time1 = prefs.notifyTime1 ?: "20:00", time2 = prefs.notifyTime2 ?: "07:30",
                    savedTime1 = prefs.notifyTime1 ?: "20:00", savedTime2 = prefs.notifyTime2 ?: "07:30",
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
                    pendingSync = if (container.profile.isGuest) emptyList() else container.outbox.pending().map { pending ->
                        PendingSyncUi(pending.opId.toString(), pending.entityType,
                            runCatching { container.outbox.payloadValue(pending) }.getOrNull(),
                            pending.action == "delete", pending.status == "conflict")
                    },
                    syncBusy = false,
                    syncError = null,
                    signedIn = !container.profile.isGuest,
                    reportNote = mutable.value.reportNote,
                    reportThread = mutable.value.reportThread,
                    supportThreads = mutable.value.supportThreads,
                    selectedSupportThreadId = mutable.value.selectedSupportThreadId,
                    supportLoading = mutable.value.supportLoading,
                    supportLoaded = mutable.value.supportLoaded,
                    supportError = mutable.value.supportError,
                    reportSending = mutable.value.reportSending,
                    reportSuccessVersion = mutable.value.reportSuccessVersion,
                    reportSuccessKey = mutable.value.reportSuccessKey,
                    reportSuccessDraftRevision = mutable.value.reportSuccessDraftRevision
                )
            }
            if (ticket != reloadTicket) return
            mutable.update { cur ->
                val editingTimes = cur.timeDirty || cur.timeSaving || cur.timeError != null
                snap.copy(
                    subgroupImpact = cur.subgroupImpact?.takeIf { cur.profileName == snap.profileName && cur.groupId == snap.groupId &&
                        it.beforeChoices == snap.subgroupChoices },
                    subgroupImpactLoading = cur.subgroupImpactLoading && cur.profileName == snap.profileName && cur.groupId == snap.groupId,
                    refreshing = cur.refreshing,
                    syncBusy = cur.syncBusy,
                    syncError = cur.syncError,
                    cloudSync = cur.cloudSync,
                    preferencePending = cur.preferencePending,
                    preferenceErrors = cur.preferenceErrors,
                    parityInvert = if ("parity" in cur.preferencePending) cur.parityInvert else snap.parityInvert,
                    theme = if ("theme" in cur.preferencePending) cur.theme else snap.theme,
                    animations = if ("animations" in cur.preferencePending) cur.animations else snap.animations,
                    notifyEnabled = if ("notify" in cur.preferencePending) cur.notifyEnabled else snap.notifyEnabled,
                    useUniversityXml = if ("source" in cur.preferencePending) cur.useUniversityXml else snap.useUniversityXml,
                    mapsAlpha = if ("maps" in cur.preferencePending) cur.mapsAlpha else snap.mapsAlpha,
                    time1 = if (editingTimes) cur.time1 else snap.time1,
                    time2 = if (editingTimes) cur.time2 else snap.time2,
                    timeError = if (editingTimes) cur.timeError else snap.timeError,
                    timeDirty = if (editingTimes) cur.timeDirty else false,
                    timeSaving = cur.timeSaving,
                    timeSaveError = cur.timeSaveError,
                    reportNote = cur.reportNote,
                    reportThread = cur.reportThread,
                    supportThreads = cur.supportThreads,
                    selectedSupportThreadId = cur.selectedSupportThreadId,
                    supportLoading = cur.supportLoading,
                    supportLoaded = cur.supportLoaded,
                    supportError = cur.supportError,
                    reportSending = cur.reportSending,
                    reportSuccessVersion = cur.reportSuccessVersion,
                    reportSuccessKey = cur.reportSuccessKey,
                    reportSuccessDraftRevision = cur.reportSuccessDraftRevision,
                    undoSubgroup = cur.undoSubgroup?.takeIf { undo ->
                        undo.groupId == snap.groupId && undo.profile == container.profile.databaseName &&
                            snap.subgroupChoices[undo.streamId] == undo.after
                    }
                )
            }
            if (snap.signedIn && !mutable.value.supportLoaded && !mutable.value.supportLoading)
                viewModelScope.launch { refreshSupport() }
            if (!snap.signedIn) {
                ++supportLoadTicket
                supportData = emptyList()
                supportAcknowledged.clear()
                mutable.update { it.copy(supportThreads = emptyList(), selectedSupportThreadId = null,
                    reportThread = emptyList(), supportError = null, supportLoading = false, supportLoaded = false) }
            }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSettings", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    private fun supportScopeCurrent(profile: String): Boolean =
        !container.profile.isGuest && container.profile.databaseName == profile &&
            (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container
                ?.let { it === container } != false

    private suspend fun refreshSupport() {
        val client = container.accounts ?: run {
            mutable.update { it.copy(supportLoaded = true,
                supportError = container.app.getString(R.string.uxnext_support_history_failed)) }
            return
        }
        val profile = container.profile.databaseName
        val ticket = ++supportLoadTicket
        val wasLoaded = mutable.value.supportLoaded
        mutable.update { it.copy(supportLoading = true, supportError = null) }
        try {
            val rows = withContext(Dispatchers.IO) {
                val token = container.accessToken() ?: error("support session unavailable")
                client.supportThreads(token)
            }
            if (ticket != supportLoadTicket || !supportScopeCurrent(profile)) return
            supportData = mergeSupportAcknowledged(rows, supportAcknowledged.values)
            supportAcknowledged.keys.removeAll { id ->
                val acknowledged = supportAcknowledged[id] ?: return@removeAll false
                supportThreadContainsAck(rows.firstOrNull { it.id == id }, acknowledged)
            }
            mutable.update { state ->
                val selected = selectedSupportThreadId(supportData, state.selectedSupportThreadId, wasLoaded)
                state.copy(supportThreads = supportData.map { SupportThreadUi(it.id, it.subject, it.messages.size) },
                    selectedSupportThreadId = selected,
                    reportThread = supportData.firstOrNull { it.id == selected }?.messages?.map { message ->
                        ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note(message.author, supportText(message))
                    }.orEmpty(), supportLoading = false, supportLoaded = true, supportError = null)
            }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSettings", "support", e)
            if (ticket == supportLoadTicket && supportScopeCurrent(profile)) mutable.update {
                it.copy(supportLoading = false, supportLoaded = true,
                    supportError = container.app.getString(R.string.uxnext_support_history_failed))
            }
        }
    }

    private fun selectSupportThread(id: String?) {
        if (id != null && supportData.none { it.id == id }) return
        mutable.update { it.copy(selectedSupportThreadId = id,
            reportThread = supportData.firstOrNull { row -> row.id == id }?.messages?.map { message ->
                ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note(message.author, supportText(message))
            }.orEmpty(), reportNote = "") }
    }

    private fun supportText(message: ru.bgtu_voenmeh.zapara.data.accounts.SupportMessage): String {
        val extra = message.attachments.joinToString("\n") {
            container.app.getString(if (it.kind == "photo") R.string.face_photo_named else R.string.face_log_named, it.name)
        }
        return if (extra.isEmpty()) message.body else message.body + "\n" + extra
    }

    private fun report(subject: String, body: String, photos: List<Pair<String, ByteArray>>, draftRevision: Long?) {
        if (mutable.value.reportSending) return
        val selectedId = mutable.value.selectedSupportThreadId
        val selected = selectedId?.let { id -> supportData.firstOrNull { it.id == id } }
        if (selectedId != null && selected == null) return
        if (!SupportInputLimits.evaluate(selected?.subject ?: subject, body, selectedId != null).canSend) {
            mutable.update { it.copy(reportNote = container.app.getString(R.string.ux60_support_input_failed)) }
            return
        }
        val local = ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.submit(
            mutable.value.signedIn, mutable.value.reportThread, selected?.subject ?: subject, body,
            container.app.getString(R.string.face_support_sign_in),
            container.app.getString(R.string.face_support_describe)
        )
        if (local.error != null) {
            mutable.update { it.copy(reportNote = local.error) }
            return
        }
        val client = container.accounts
        val profile = container.profile.databaseName
        if (!supportScopeCurrent(profile)) return
        val key = selectedId ?: "new"
        mutable.update { it.copy(reportSending = true, reportNote = "") }
        viewModelScope.launch {
            try {
                val token = container.accessToken()
                if (client == null || token.isNullOrEmpty()) {
                    mutable.update { it.copy(reportNote = container.app.getString(R.string.face_support_sign_in), reportSending = false) }
                    return@launch
                }
                val saved = withContext(Dispatchers.IO) {
                    val logs = SupportLogs.collect()
                    if (selectedId == null) client.openSupport(token, subject.trim(), body.trim(), photos, logs)
                    else client.continueSupport(token, selectedId, body.trim(), photos, logs)
                }
                if (!supportScopeCurrent(profile)) return@launch
                // An older GET cannot replace the just acknowledged thread or its new reply.
                ++supportLoadTicket
                supportData = mergeSupportThread(supportData, saved)
                supportAcknowledged[saved.id] = saved
                mutable.update {
                    it.copy(
                        reportNote = "",
                        reportSending = false,
                        supportLoading = false,
                        supportError = null,
                        reportSuccessVersion = it.reportSuccessVersion + 1,
                        reportSuccessKey = key,
                        reportSuccessDraftRevision = draftRevision,
                        supportThreads = supportData.map { row -> SupportThreadUi(row.id, row.subject, row.messages.size) },
                        selectedSupportThreadId = if (it.selectedSupportThreadId == selectedId) saved.id else it.selectedSupportThreadId,
                        reportThread = if (it.selectedSupportThreadId == selectedId) saved.messages.map { line ->
                            ru.bgtu_voenmeh.zapara.ui.chat.SupportForm.Note(line.author, supportText(line))
                        } else it.reportThread
                    )
                }
                if (!mutable.value.supportLoaded) viewModelScope.launch { refreshSupport() }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSettings", "support send", e)
                if (supportScopeCurrent(profile)) mutable.update { it.copy(
                    reportNote = container.app.getString(R.string.face_support_failed), reportSending = false) }
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
