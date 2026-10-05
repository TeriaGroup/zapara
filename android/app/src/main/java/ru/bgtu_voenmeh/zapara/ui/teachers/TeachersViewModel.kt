package ru.bgtu_voenmeh.zapara.ui.teachers

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
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.ui.LessonFormat

class TeachersViewModel(private val container: AppContainer) : ViewModel() {
    private data class SearchSnapshot(val rows: List<TeacherRowUi>, val total: Int,
        val myIds: Set<String>, val context: SchedCtx, val selectedLessons: List<LecturerLesson>?)
    private val mutable = MutableStateFlow(TeachersUiState())
    val state: StateFlow<TeachersUiState> = mutable.asStateFlow()
    private var searchTicket = 0
    private var detailTicket = 0
    private var groupEpoch = 0L

    init {
        viewModelScope.launch { load() }
        viewModelScope.launch { container.events.events.collect { search() } }
    }

    fun onEvent(event: TeachersEvent) {
        when (event) {
            is TeachersEvent.Query -> {
                ++searchTicket
                mutable.update { it.copy(query = event.value, searching = true) }
                viewModelScope.launch { search() }
            }
            is TeachersEvent.OnlyMine -> {
                ++searchTicket
                mutable.update { it.copy(onlyMine = event.value, searching = true) }
                viewModelScope.launch { search() }
            }
            is TeachersEvent.Open -> viewModelScope.launch { open(event.id) }
            TeachersEvent.Back -> {
                ++detailTicket
                mutable.update { it.copy(selected = null, details = emptyList(), detailsLoading = false) }
            }
            is TeachersEvent.Parity -> viewModelScope.launch {
                mutable.update { it.copy(parityFilter = event.index) }
                mutable.value.selected?.let { open(it.id) }
            }
            TeachersEvent.Retry -> viewModelScope.launch {
                val selected = mutable.value.selected
                if (selected != null) open(selected.id) else load()
            }
        }
    }

    private suspend fun load() {
        val ticket = ++searchTicket
        mutable.update { it.copy(loadError = null) }
        try {
            withContext(Dispatchers.IO) { container.lecturerStore.load() }
            if (ticket == searchTicket) search()
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaTeachers", "load", e)
            if (ticket == searchTicket) mutable.update { it.copy(loaded = true, searching = false,
                loadError = container.app.getString(R.string.ux30_teachers_load_failed)) }
        }
    }

    private suspend fun search() {
        val ticket = ++searchTicket
        val query = mutable.value.query
        val onlyMine = mutable.value.onlyMine
        val selectedId = mutable.value.selected?.id
        mutable.update { it.copy(searching = true) }
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                val gname = container.repo.groups().firstOrNull { it.id == gid }?.name
                val myIds = if (gid.isEmpty()) emptySet() else
                    container.lecturerStore.myTeacherIds(container.ownLessons(), gid, gname)
                val found = container.lecturerStore.search(query, onlyMine, myIds)
                val rows = found.map { lect ->
                    val subjects = container.lecturerStore.lessonsFor(lect.id)
                        .map { LessonFormat.stripType(it.disciplineRaw.ifBlank { it.subjectRaw }, it.typeRaw) }
                        .distinct().take(4).joinToString(" · ")
                    TeacherRowUi(lect.id, lect.name, subjects, lect.id in myIds || lect.name in myIds,
                        lect.kafedra)
                }
                SearchSnapshot(rows, container.lecturerStore.lecturers().size, myIds,
                    SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert),
                    selectedId?.let(container.lecturerStore::lessonsFor))
            }
            if (ticket != searchTicket || withContext(Dispatchers.IO) {
                container.repo.settings().myGroupId.orEmpty()
            } != snap.context.groupId) return
            if (snap.context.groupId != mutable.value.groupId ||
                container.profile.databaseName != mutable.value.profileName) {
                ++groupEpoch; ++detailTicket
            }
            mutable.update { state -> state.copy(loaded = true, list = snap.rows, total = snap.total,
                appliedQuery = query, appliedOnlyMine = onlyMine, searching = false,
                myIds = snap.myIds, groupId = snap.context.groupId,
                profileName = container.profile.databaseName, loadError = null,
                details = if (state.selected?.id == selectedId && snap.selectedLessons != null)
                    TeacherDetailsComposer.compose(snap.selectedLessons, state.parityFilter,
                        snap.context.groupId, container.copy, snap.context.invert,
                        container.clock().toLocalDate(), snap.context) else state.details) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaTeachers", "search", e)
            if (ticket == searchTicket) mutable.update { it.copy(loaded = true, searching = false,
                loadError = container.app.getString(R.string.ux30_teachers_load_failed)) }
        }
    }

    private suspend fun open(id: String) {
        val row = mutable.value.list.firstOrNull { it.id == id } ?: return
        val request = TeacherDetailRequest(++detailTicket, id, mutable.value.groupId,
            container.profile.databaseName, groupEpoch)
        mutable.update { it.copy(selected = row, details = emptyList(), detailsLoading = true) }
        try {
            val (context, lessons) = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                SchedCtx(prefs.myGroupId.orEmpty(), prefs.periodStart, prefs.weekCount, prefs.parityInvert) to
                    container.lecturerStore.lessonsFor(id)
            }
            if (context.groupId != request.groupId || container.profile.databaseName != request.profileName ||
                withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } != request.groupId) {
                if (request.matches(detailTicket, mutable.value.selected))
                    mutable.update { it.copy(detailsLoading = false) }
                search()
                return
            }
            mutable.update { state ->
                if (request.matches(detailTicket, state.selected, state.groupId, state.profileName, groupEpoch))
                    state.copy(details = TeacherDetailsComposer.compose(lessons, state.parityFilter,
                        context.groupId, container.copy, context.invert,
                        container.clock().toLocalDate(), context),
                        groupId = context.groupId, profileName = container.profile.databaseName,
                        detailsLoading = false, loadError = null)
                else state
            }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaTeachers", "open", e)
            val currentGroup = try { withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } }
                catch (_: Exception) { null }
            if (currentGroup != request.groupId || container.profile.databaseName != request.profileName) return
            mutable.update { state -> if (request.matches(detailTicket, state.selected,
                state.groupId, state.profileName, groupEpoch))
                state.copy(detailsLoading = false,
                    loadError = container.app.getString(R.string.ux30_teachers_load_failed)) else state }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = TeachersViewModel(container) as T
        }
    }
}
