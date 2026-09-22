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
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.ui.AppEvent

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
            FriendsEvent.Add -> {
                if (!mutable.value.canAdd) return
                val used = mutable.value.friends.map { FriendPalette.keys[it.colorIndex] }
                mutable.update {
                    it.copy(editor = FriendEditorUi(null, null, "", "", FriendPalette.indexOf(FriendPalette.firstFree(used))))
                }
            }
            is FriendsEvent.Edit -> {
                val f = mutable.value.friends.getOrNull(event.index) ?: return
                mutable.update {
                    it.copy(editor = FriendEditorUi(f.id, f.index, f.groupName, f.members, f.colorIndex))
                }
            }
            is FriendsEvent.EditorGroup -> mutable.update { s -> s.copy(editor = s.editor?.copy(groupName = event.name, pickerOpen = false)) }
            is FriendsEvent.EditorMembers -> mutable.update { s -> s.copy(editor = s.editor?.copy(members = event.text)) }
            is FriendsEvent.EditorColor -> mutable.update { s -> s.copy(editor = s.editor?.copy(colorIndex = event.index)) }
            FriendsEvent.OpenPicker -> mutable.update { s -> s.copy(editor = s.editor?.copy(pickerOpen = true)) }
            FriendsEvent.ClosePicker -> mutable.update { s -> s.copy(editor = s.editor?.copy(pickerOpen = false)) }
            FriendsEvent.EditorSave -> saveEditor()
            FriendsEvent.EditorCancel -> mutable.update { it.copy(editor = null) }
            is FriendsEvent.Toggle -> viewModelScope.launch {
                val id = mutable.value.friends.getOrNull(event.index)?.id ?: return@launch
                writes.withLock {
                    withContext(Dispatchers.IO) { edits.toggle(id, event.enabled) }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
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
        saving = true
        mutable.update { it.copy(editor = null) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        val hex = FriendPalette.keys[editor.colorIndex.coerceIn(0, 4)]
                        edits.save(editor.id, editor.groupName, editor.members, hex)
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
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
                val preview = FriendsPreview.line(
                    today = container.clock().toLocalDate(),
                    myGroupId = prefs.myGroupId.orEmpty(),
                    friends = friendModels,
                    strictness = Strictness.nearest(prefs.intersectionStrictness),
                    periodStart = prefs.periodStart,
                    weekCount = prefs.weekCount,
                    invert = prefs.parityInvert,
                    allForGroup = { id ->
                        val rows = container.repo.allForGroup(id)
                        if (id == prefs.myGroupId) ru.bgtu_voenmeh.zapara.data.Subgroups.visible(rows, container.subgroupChoices(id)) else rows
                    },
                    resolveId = { name -> groups.firstOrNull { it.name == name }?.id },
                    copy = container.copy
                )
                FriendsUiState(
                    loaded = true, friends = friends, canAdd = friends.size < 5,
                    strictness = Strictness.nearest(prefs.intersectionStrictness),
                    alwaysShow = prefs.alwaysShowAllTrafficLights, invert = prefs.parityInvert,
                    groups = groups,
                    previewLine = preview
                )
            }
            if (ticket != reloadTicket) return
            mutable.update { cur -> snap.copy(editor = cur.editor, confirmDelete = cur.confirmDelete) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaFriends", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = FriendsViewModel(container) as T
        }
    }
}
