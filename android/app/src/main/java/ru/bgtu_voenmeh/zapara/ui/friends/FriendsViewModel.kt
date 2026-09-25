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
    private var reloadTicket = 0

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: FriendsEvent) {
        when (event) {
            FriendsEvent.Retry -> viewModelScope.launch { reload() }
            FriendsEvent.RefreshSchedules -> refreshSchedules()
            FriendsEvent.Add -> {
                if (!mutable.value.canAdd) return
                val used = mutable.value.friends.map { FriendPalette.keys[it.colorIndex] }
                mutable.update {
                    it.copy(editor = FriendEditorUi(null, null, "", "", FriendPalette.indexOf(FriendPalette.firstFree(used))), editorError = null)
                }
            }
            is FriendsEvent.Edit -> {
                val f = mutable.value.friends.getOrNull(event.index) ?: return
                mutable.update {
                    it.copy(editor = FriendEditorUi(f.id, f.index, f.groupName, f.members, f.colorIndex), editorError = null)
                }
            }
            is FriendsEvent.EditorGroup -> mutable.update { s -> s.copy(editor = s.editor?.copy(groupName = event.name, pickerOpen = false), editorError = null) }
            is FriendsEvent.EditorMembers -> mutable.update { s -> s.copy(editor = s.editor?.copy(members = event.text)) }
            is FriendsEvent.EditorColor -> mutable.update { s -> s.copy(editor = s.editor?.copy(colorIndex = event.index)) }
            FriendsEvent.OpenPicker -> mutable.update { s -> s.copy(editor = s.editor?.copy(pickerOpen = true)) }
            FriendsEvent.ClosePicker -> mutable.update { s -> s.copy(editor = s.editor?.copy(pickerOpen = false)) }
            FriendsEvent.EditorSave -> saveEditor()
            FriendsEvent.EditorCancel -> mutable.update { it.copy(editor = null, editorError = null) }
            is FriendsEvent.Toggle -> viewModelScope.launch {
                val id = mutable.value.friends.getOrNull(event.index)?.id ?: return@launch
                writes.withLock {
                    withContext(Dispatchers.IO) { edits.toggle(id, event.enabled) }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
                if (event.enabled) refreshNeededSchedules()
            }
            is FriendsEvent.AskDelete -> mutable.update { it.copy(confirmDelete = event.id) }
            FriendsEvent.ConfirmDelete -> {
                val id = mutable.value.confirmDelete ?: return
                mutable.update { it.copy(confirmDelete = null, editor = null) }
                viewModelScope.launch {
                    writes.withLock { withContext(Dispatchers.IO) { edits.delete(id) } }
                    container.events.emit(AppEvent.PersonalizationChanged)
                }
            }
            FriendsEvent.CancelDelete -> mutable.update { it.copy(confirmDelete = null) }
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
        val selected = mutable.value.groups.firstOrNull {
            it.id == editor.groupName || it.name.equals(editor.groupName, ignoreCase = true)
        }
        val current = mutable.value.friends.firstOrNull { it.id == editor.id }
        val groupName = selected?.name ?: current?.groupName?.takeIf { it.equals(editor.groupName, ignoreCase = true) }
        val duplicate = mutable.value.friends.any { friend ->
            friend.id != editor.id && (friend.groupName.equals(groupName, ignoreCase = true) ||
                (selected != null && mutable.value.groups.firstOrNull { it.name.equals(friend.groupName, ignoreCase = true) }?.id == selected.id))
        }
        if (groupName == null || selected?.id == mutable.value.myGroupId || duplicate) {
            mutable.update { it.copy(editorError = container.app.getString(R.string.friends_group_unavailable)) }
            return
        }
        val needsSchedule = editor.id == null || current?.groupName != groupName
        saving = true
        mutable.update { it.copy(editor = null, editorError = null) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        val hex = FriendPalette.keys[editor.colorIndex.coerceIn(0, 4)]
                        edits.save(editor.id, groupName, editor.members, hex)
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
                if (needsSchedule) viewModelScope.launch { refreshNeededSchedules() }
            } catch (e: CancellationException) {
                mutable.update { cur -> if (cur.editor == null) cur.copy(editor = editor) else cur }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaFriends", "save", e)
                mutable.update { cur -> if (cur.editor == null) cur.copy(editor = editor) else cur }
            } finally {
                saving = false
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
                    loaded = true, friends = friends, canAdd = friends.size < 5, myGroupId = myGroup,
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
            mutable.update { cur -> snap.copy(editor = cur.editor, confirmDelete = cur.confirmDelete,
                editorError = cur.editorError, refreshing = cur.refreshing, refreshFailed = cur.refreshFailed) }
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
