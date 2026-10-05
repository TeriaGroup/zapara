package ru.bgtu_voenmeh.zapara.ui.friends

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
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.api.TimetableApiCache
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat

class FriendsViewModel(private val container: AppContainer) : ViewModel() {
    private val edits = FriendEditorActions(container.repo)
    private val mutable = MutableStateFlow(FriendsUiState())
    val state: StateFlow<FriendsUiState> = mutable.asStateFlow()
    private val writes = Mutex()
    private var saving = false
    private var deleteScope: FriendActionScope? = null
    private var reloadTicket = 0
    private var editRequestVersion = 0

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: FriendsEvent) {
        when (event) {
            is FriendsEvent.MeetingDate -> {
                mutable.update { it.copy(meetingDate = event.date, meetingWindows = emptyList()) }
                viewModelScope.launch { reload() }
            }
            FriendsEvent.Retry -> viewModelScope.launch { reload() }
            FriendsEvent.RefreshSchedules -> refreshSchedules()
            FriendsEvent.Add -> {
                if (!mutable.value.canAdd) return
                editRequestVersion++
                val used = mutable.value.friends.map { FriendPalette.keys[it.colorIndex] }
                mutable.update {
                    it.copy(editor = FriendEditorUi(null, null, "", "", FriendPalette.indexOf(FriendPalette.firstFree(used)),
                        it.myGroupId, it.profileName), editorError = null)
                }
            }
            is FriendsEvent.Edit -> {
                val scope = event.scope
                if (!scope.current(mutable.value.myGroupId, mutable.value.profileName)) return
                val ticket = reloadTicket
                val request = ++editRequestVersion
                viewModelScope.launch {
                    try {
                    val valid = withContext(Dispatchers.IO) {
                        scope.current(container.repo.settings().myGroupId.orEmpty(), container.profile.databaseName)
                    }
                    if (!valid || ticket != reloadTicket || request != editRequestVersion ||
                        !scope.current(mutable.value.myGroupId, mutable.value.profileName)) return@launch
                    val f = scope.find(mutable.value.friends) ?: return@launch
                    mutable.update {
                        it.copy(editor = FriendEditorUi(f.id, f.index, f.groupName, f.members, f.colorIndex,
                            scope.groupId, scope.profileName), editorError = null)
                    }
                    } catch (e: CancellationException) { throw e }
                    catch (e: Exception) {
                        android.util.Log.w("ZaparaFriends", "open editor", e)
                        container.toasts.show(container.app.getString(R.string.ux60_friend_open_failed),
                            ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Bad)
                    }
                }
            }
            is FriendsEvent.EditorGroup -> if (!saving) mutable.update { s -> s.copy(editor = s.editor?.copy(groupName = event.name, pickerOpen = false), editorError = null) }
            is FriendsEvent.ManualOffline -> mutable.update { s -> s.copy(editor = s.editor?.copy(
                manualOffline = event.enabled, groupName = if (event.enabled) s.editor.groupName else ""), editorError = null) }
            is FriendsEvent.EditorMembers -> if (!saving) mutable.update { s -> s.copy(editor = s.editor?.copy(members = event.text)) }
            is FriendsEvent.EditorColor -> if (!saving) mutable.update { s -> s.copy(editor = s.editor?.copy(colorIndex = event.index)) }
            FriendsEvent.OpenPicker -> if (!saving) mutable.update { s -> s.copy(editor = s.editor?.copy(pickerOpen = true)) }
            FriendsEvent.ClosePicker -> if (!saving) mutable.update { s -> s.copy(editor = s.editor?.copy(pickerOpen = false)) }
            FriendsEvent.EditorSave -> saveEditor()
            FriendsEvent.EditorCancel -> if (!saving) {
                editRequestVersion++
                mutable.update { it.copy(editor = null, editorError = null) }
            }
            is FriendsEvent.Toggle -> viewModelScope.launch {
                try {
                val scope = event.scope
                if (!scope.current(mutable.value.myGroupId, mutable.value.profileName) ||
                    scope.find(mutable.value.friends) == null) return@launch
                val saved = writes.withLock {
                    withContext(Dispatchers.IO) {
                        if (!scope.current(container.repo.settings().myGroupId.orEmpty(), container.profile.databaseName) ||
                            container.repo.friends().none { it.id == scope.id }) false
                        else { edits.toggle(scope.id, event.enabled); true }
                    }
                }
                if (!saved) return@launch
                container.events.emit(AppEvent.PersonalizationChanged)
                if (event.enabled) refreshNeededSchedules()
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) {
                    android.util.Log.w("ZaparaFriends", "toggle", e)
                    container.toasts.show(container.app.getString(R.string.ux60_friend_toggle_failed),
                        ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Bad)
                }
            }
            is FriendsEvent.AskDelete -> {
                if (saving || mutable.value.deletePending || mutable.value.friends.none { it.id == event.id }) return
                deleteScope = FriendActionScope(event.id, mutable.value.myGroupId, mutable.value.profileName)
                mutable.update { it.copy(confirmDelete = event.id, deleteError = null) }
            }
            FriendsEvent.ConfirmDelete -> {
                val scope = deleteScope ?: return
                if (mutable.value.confirmDelete != scope.id || mutable.value.deletePending) return
                mutable.update { it.copy(deletePending = true, deleteError = null) }
                viewModelScope.launch {
                    try {
                        val deleted = writes.withLock { withContext(Dispatchers.IO) {
                            if (!scope.current(container.repo.settings().myGroupId.orEmpty(), container.profile.databaseName) ||
                                container.repo.friends().none { it.id == scope.id }) false
                            else { edits.delete(scope.id); true }
                        } }
                        if (!deleted) throw IllegalStateException("friend target changed")
                        if (deleteScope == scope) {
                            deleteScope = null
                            mutable.update { it.deleteAcknowledged(scope.id) }
                        }
                        container.events.emit(AppEvent.PersonalizationChanged)
                    } catch (e: CancellationException) { throw e }
                    catch (e: Exception) {
                        android.util.Log.w("ZaparaFriends", "delete", e)
                        if (deleteScope == scope) mutable.update { it.deleteFailed(scope.id,
                            container.app.getString(R.string.ux60_friend_delete_failed)) }
                    } finally { mutable.update { it.copy(deletePending = false) } }
                }
            }
            FriendsEvent.CancelDelete -> if (!mutable.value.deletePending) {
                deleteScope = null
                mutable.update { it.copy(confirmDelete = null, deleteError = null) }
            }
            is FriendsEvent.Strictness -> viewModelScope.launch {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        val s = container.repo.settings()
                        container.repo.saveSettings(s.copy(intersectionStrictness = Strictness.nearest(event.value)))
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            is FriendsEvent.AlwaysShow -> viewModelScope.launch {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        val s = container.repo.settings()
                        container.repo.saveSettings(s.copy(alwaysShowAllTrafficLights = event.value))
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            is FriendsEvent.Invert -> viewModelScope.launch {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        val s = container.repo.settings()
                        container.repo.saveSettings(s.copy(parityInvert = event.value))
                    }
                }
                container.events.emit(AppEvent.ScheduleChanged)
            }
        }
    }

    private fun saveEditor() {
        if (saving) return
        val editor = mutable.value.editor ?: return
        if (editor.groupName.isBlank()) return
        if (!FriendActionScope(editor.id ?: -1, editor.sourceGroupId, editor.sourceProfileName)
                .current(mutable.value.myGroupId, mutable.value.profileName)) {
            mutable.update { it.copy(editorError = container.app.getString(R.string.ux60_friend_scope_changed)) }
            return
        }
        val selected = mutable.value.groups.firstOrNull {
            it.id == editor.groupName || it.name.equals(editor.groupName, ignoreCase = true)
        }
        val current = mutable.value.friends.firstOrNull { it.id == editor.id }
        val groupName = validFriendGroupName(editor.groupName, mutable.value.groups,
            editor.manualOffline, current?.groupName)
        val duplicate = mutable.value.friends.any { friend ->
            friend.id != editor.id && (friend.groupName.equals(groupName, ignoreCase = true) ||
                (selected != null && mutable.value.groups.firstOrNull { it.name.equals(friend.groupName, ignoreCase = true) }?.id == selected.id))
        }
        if (groupName == null || selected?.id == mutable.value.myGroupId ||
            groupName == mutable.value.myGroupId || duplicate) {
            mutable.update { it.copy(editorError = container.app.getString(R.string.friends_group_unavailable)) }
            return
        }
        val needsSchedule = editor.id == null || current?.groupName != groupName
        saving = true
        mutable.update { it.copy(editorSaving = true, editorError = null) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        if (container.repo.settings().myGroupId.orEmpty() != editor.sourceGroupId ||
                            container.profile.databaseName != editor.sourceProfileName)
                            throw IllegalStateException("friend editor scope changed")
                        val hex = FriendPalette.keys[editor.colorIndex.coerceIn(0, 4)]
                        edits.save(editor.id, groupName, editor.members, hex)
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
                mutable.update { state -> if (state.editor == editor)
                    state.copy(editor = null, editorError = null) else state }
                if (needsSchedule) viewModelScope.launch { refreshNeededSchedules() }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaFriends", "save", e)
                mutable.update { cur -> if (cur.editor == editor)
                    cur.copy(editorError = container.app.getString(R.string.uxnext_friend_save_failed)) else cur }
            } finally {
                saving = false
                mutable.update { it.copy(editorSaving = false) }
            }
        }
    }

    private suspend fun refreshNeededSchedules() {
        if (!ScheduleRepository.networkEnabled || !container.api.configured) return
        try {
            if (withContext(Dispatchers.IO) { container.api.refresh(neededOnly = true) })
                container.events.emit(AppEvent.ScheduleChanged)
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) { android.util.Log.w("ZaparaFriends", "refresh needed", e) }
    }

    private fun refreshSchedules() {
        if (mutable.value.refreshing) return
        mutable.update { it.copy(refreshing = true, refreshFailed = false) }
        viewModelScope.launch {
            try {
                val ok = ScheduleRepository.networkEnabled && withContext(Dispatchers.IO) { container.timetable.pull() }
                if (ok) container.events.emit(AppEvent.ScheduleChanged)
                else mutable.update { it.copy(refreshFailed = true) }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaFriends", "refresh", e)
                mutable.update { it.copy(refreshFailed = true) }
            } finally {
                mutable.update { it.copy(refreshing = false) }
                reload()
            }
        }
    }

    private suspend fun reload() {
        val ticket = ++reloadTicket
        val meetingDate = mutable.value.meetingDate
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val rows = container.db.friendDao().getAll()
                val friends = rows.mapIndexed { i, e ->
                    FriendUi(e.id, i, e.groupName, e.memberNames, e.enabled, FriendPalette.indexOf(e.colorHex))
                }
                val groups = container.repo.groups()
                val friendModels = rows.map { Friend(it.groupName, it.colorHex, it.enabled, it.memberNames) }
                val myGroup = prefs.myGroupId.orEmpty()
                val rawCache = mutableMapOf<String, List<Lesson>>()
                fun raw(id: String): List<Lesson> = rawCache.getOrPut(id) { container.repo.allForGroup(id) }
                val scheduleCache = mutableMapOf<String, List<Lesson>>()
                fun schedule(id: String): List<Lesson> = scheduleCache.getOrPut(id) {
                    val lessons = raw(id)
                    if (id == myGroup) ru.bgtu_voenmeh.zapara.data.Subgroups.visible(lessons, container.subgroupChoices(id)) else lessons
                }
                fun hasSchedule(id: String) = container.db.apiCacheMetadataDao().get(id) != null || raw(id).isNotEmpty()
                val apiCache = TimetableApiCache(container.repo.store)
                fun intervals(id: String) = ru.bgtu_voenmeh.zapara.data.Schedule.lessonsForDate(
                    schedule(id), id, meetingDate, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                    .map { ru.bgtu_voenmeh.zapara.ui.StudyInterval(it.timeStart, it.timeEnd) }
                val meetings = if (myGroup.isBlank() || !hasSchedule(myGroup)) emptyList() else rows.filter { it.enabled }.mapNotNull { friend ->
                    val id = groups.firstOrNull { it.id == friend.groupName || it.name.equals(friend.groupName, ignoreCase = true) }?.id
                        ?: return@mapNotNull null
                    if (id == myGroup || !hasSchedule(id) || !apiCache.canIntersect(myGroup, id)) return@mapNotNull null
                    FriendMeetingWindows(friend.groupName, ru.bgtu_voenmeh.zapara.ui.StudyPlanning.commonFreeIntervals(intervals(myGroup), intervals(id), 1))
                }
                val forecast = FriendsPreview.forecast(
                    now = container.clock(),
                    myGroupId = prefs.myGroupId.orEmpty(),
                    friends = friendModels,
                    strictness = Strictness.nearest(prefs.intersectionStrictness),
                    periodStart = prefs.periodStart,
                    weekCount = prefs.weekCount,
                    invert = prefs.parityInvert,
                    allForGroup = ::schedule,
                    resolveId = { name -> groups.firstOrNull { it.id == name || it.name.equals(name, ignoreCase = true) }?.id },
                    hasSchedule = ::hasSchedule,
                    canIntersect = apiCache::canIntersect
                )
                val first = forecast.encounters.firstOrNull()
                val preview = first?.let {
                    container.copy.get("friends_preview", LessonFormat.weekdayShort(it.date, container.copy),
                        LessonFormat.dayMonth(it.date), it.time, it.subject)
                } ?: container.copy.get("friends_preview_none")
                FriendsUiState(
                    meetingDate = meetingDate, meetingWindows = meetings,
                    loaded = true, friends = friends, canAdd = friends.size < 5, myGroupId = myGroup,
                    profileName = container.profile.databaseName,
                    strictness = Strictness.nearest(prefs.intersectionStrictness),
                    alwaysShow = prefs.alwaysShowAllTrafficLights, invert = prefs.parityInvert,
                    groups = groups,
                    previewLine = preview,
                    encounters = forecast.encounters,
                    missingGroups = forecast.missingGroups,
                    checkedGroups = forecast.checkedGroups,
                    hasOwnSchedule = myGroup.isNotBlank() && hasSchedule(myGroup)
                )
            }
            if (ticket != reloadTicket) return
            if (deleteScope?.current(snap.myGroupId, snap.profileName) != true) deleteScope = null
            mutable.update { cur ->
                val editor = cur.editor?.takeIf { it.sourceGroupId == snap.myGroupId && it.sourceProfileName == snap.profileName }
                snap.copy(editor = editor,
                    confirmDelete = cur.confirmDelete.takeIf { deleteScope != null },
                    editorError = cur.editorError.takeIf { editor != null }, editorSaving = cur.editorSaving && editor != null,
                    deletePending = cur.deletePending && deleteScope != null,
                    deleteError = cur.deleteError.takeIf { deleteScope != null },
                    refreshing = cur.refreshing, refreshFailed = cur.refreshFailed)
            }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaFriends", "reload", e)
            mutable.update { it.copy(loaded = true, failed = true) }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = FriendsViewModel(container) as T
        }
    }
}
