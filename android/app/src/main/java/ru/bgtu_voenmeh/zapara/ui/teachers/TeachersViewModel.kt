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
import ru.bgtu_voenmeh.zapara.ui.LessonFormat

class TeachersViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(TeachersUiState())
    val state: StateFlow<TeachersUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch { load() }
        viewModelScope.launch { container.events.events.collect { search() } }
    }

    fun onEvent(event: TeachersEvent) {
        when (event) {
            is TeachersEvent.Query -> {
                mutable.update { it.copy(query = event.value) }
                viewModelScope.launch { search() }
            }
            is TeachersEvent.OnlyMine -> {
                mutable.update { it.copy(onlyMine = event.value) }
                viewModelScope.launch { search() }
            }
            is TeachersEvent.Open -> viewModelScope.launch { open(event.id) }
            TeachersEvent.Back -> mutable.update { it.copy(selected = null, details = emptyList()) }
            is TeachersEvent.Parity -> viewModelScope.launch {
                mutable.update { it.copy(parityFilter = event.index) }
                mutable.value.selected?.let { open(it.id) }
            }
        }
    }

    private suspend fun load() {
        try {
            withContext(Dispatchers.IO) { container.lecturerStore.load() }
            search()
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaTeachers", "load", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    private suspend fun search() {
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                val myIds = if (gid.isEmpty()) emptySet() else container.lecturerStore.myTeacherIds(container.repo.allForGroup(gid))
                val found = container.lecturerStore.search(mutable.value.query, mutable.value.onlyMine, myIds)
                val rows = found.map { lect ->
                    val subjects = container.lecturerStore.lessonsFor(lect.id)
                        .map { LessonFormat.stripType(it.disciplineRaw.ifBlank { it.subjectRaw }, it.typeRaw) }
                        .distinct().take(4).joinToString(" · ")
                    TeacherRowUi(lect.id, lect.name, subjects, lect.id in myIds || lect.name in myIds)
                }
                mutable.value.copy(loaded = true, list = rows, total = container.lecturerStore.lecturers().size, myIds = myIds)
            }
            mutable.value = snap
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaTeachers", "search", e)
        }
    }

    private suspend fun open(id: String) {
        val row = mutable.value.list.firstOrNull { it.id == id } ?: return
        val gid = withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() }
        val lessons = withContext(Dispatchers.IO) { container.lecturerStore.lessonsFor(id) }
        mutable.update {
            it.copy(selected = row, details = TeacherDetailsComposer.compose(lessons, it.parityFilter, gid, container.copy))
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = TeachersViewModel(container) as T
        }
    }
}
