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
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.db.FriendEntity
import ru.bgtu_voenmeh.zapara.ui.AppEvent

class FriendsViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(FriendsUiState())
    val state: StateFlow<FriendsUiState> = mutable.asStateFlow()

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
                withContext(Dispatchers.IO) {
                    val e = container.db.friendDao().getAll().firstOrNull { it.id == id } ?: return@withContext
                    container.db.friendDao().update(e.copy(enabled = event.enabled))
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            is FriendsEvent.AskDelete -> mutable.update { it.copy(confirmDelete = event.id) }
            FriendsEvent.ConfirmDelete -> viewModelScope.launch {
                val id = mutable.value.confirmDelete ?: return@launch
                withContext(Dispatchers.IO) { container.db.friendDao().delete(id) }
                mutable.update { it.copy(confirmDelete = null, editor = null) }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            FriendsEvent.CancelDelete -> mutable.update { it.copy(confirmDelete = null) }
            is FriendsEvent.Strictness -> viewModelScope.launch {
                withContext(Dispatchers.IO) {
                    val s = container.repo.settings()
                    container.repo.saveSettings(s.copy(intersectionStrictness = Strictness.nearest(event.value)))
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            is FriendsEvent.AlwaysShow -> viewModelScope.launch {
                withContext(Dispatchers.IO) {
                    val s = container.repo.settings()
                    container.repo.saveSettings(s.copy(alwaysShowAllTrafficLights = event.value))
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            is FriendsEvent.Invert -> viewModelScope.launch {
                withContext(Dispatchers.IO) {
                    val s = container.repo.settings()
                    container.repo.saveSettings(s.copy(parityInvert = event.value))
                }
                container.events.emit(AppEvent.ScheduleChanged)
            }
        }
    }

    private fun saveEditor() {
        val editor = mutable.value.editor ?: return
        if (editor.groupName.isBlank()) return
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val hex = FriendPalette.keys[editor.colorIndex.coerceIn(0, 4)]
                if (editor.id == null) {
                    container.db.friendDao().insert(
                        FriendEntity(groupName = editor.groupName, colorHex = hex, enabled = true, memberNames = editor.members)
                    )
                } else {
                    val e = container.db.friendDao().getAll().firstOrNull { it.id == editor.id } ?: return@withContext
                    container.db.friendDao().update(e.copy(groupName = editor.groupName, colorHex = hex, memberNames = editor.members))
                }
            }
            mutable.update { it.copy(editor = null) }
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    private suspend fun reload() {
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val rows = container.db.friendDao().getAll()
                val friends = rows.mapIndexed { i, e ->
                    FriendUi(e.id, i, e.groupName, e.memberNames, e.enabled, FriendPalette.indexOf(e.colorHex))
                }
                FriendsUiState(
                    loaded = true, friends = friends, canAdd = friends.size < 5,
                    editor = mutable.value.editor, confirmDelete = mutable.value.confirmDelete,
                    strictness = Strictness.nearest(prefs.intersectionStrictness),
                    alwaysShow = prefs.alwaysShowAllTrafficLights, invert = prefs.parityInvert,
                    groups = container.repo.groups()
                )
            }
            mutable.value = snap
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
