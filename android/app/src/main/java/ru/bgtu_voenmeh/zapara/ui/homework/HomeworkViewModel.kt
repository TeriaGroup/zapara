package ru.bgtu_voenmeh.zapara.ui.homework

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
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import java.time.LocalDate

class HomeworkViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(HomeworkUiState())
    val state: StateFlow<HomeworkUiState> = mutable.asStateFlow()
    private var collapsed = setOf(GroupStatus.Done)

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: HomeworkEvent) {
        when (event) {
            is HomeworkEvent.ToggleDone -> viewModelScope.launch {
                withContext(Dispatchers.IO) {
                    val hw = container.homework.all().firstOrNull { it.id == event.id } ?: return@withContext
                    container.homework.markDone(event.id, !hw.done)
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            is HomeworkEvent.Edit -> openEdit(event.id)
            HomeworkEvent.Add -> openPicker()
            is HomeworkEvent.Query -> mutable.update { s ->
                s.copy(subjectPicker = s.subjectPicker?.copy(query = event.value))
            }
            is HomeworkEvent.PickSubject -> openNew(event.raw)
            HomeworkEvent.ClosePicker -> mutable.update { it.copy(subjectPicker = null) }
            is HomeworkEvent.EditorText -> mutable.update { s -> s.copy(editor = s.editor?.withText(event.text)) }
            HomeworkEvent.Inc -> mutable.update { s -> s.copy(editor = s.editor?.inc()) }
            HomeworkEvent.Dec -> mutable.update { s -> s.copy(editor = s.editor?.dec()) }
            HomeworkEvent.Save -> save()
            HomeworkEvent.Cancel -> mutable.update { it.copy(editor = null) }
            is HomeworkEvent.AskDelete -> mutable.update { it.copy(confirmDelete = event.id) }
            HomeworkEvent.ConfirmDelete -> viewModelScope.launch {
                val id = mutable.value.confirmDelete ?: return@launch
                withContext(Dispatchers.IO) { container.homework.delete(id) }
                mutable.update { it.copy(confirmDelete = null) }
                container.events.emit(AppEvent.PersonalizationChanged)
            }
            HomeworkEvent.CancelDelete -> mutable.update { it.copy(confirmDelete = null) }
            is HomeworkEvent.ToggleGroup -> {
                collapsed = if (event.status in collapsed) collapsed - event.status else collapsed + event.status
                mutable.update { s ->
                    s.copy(groups = s.groups.map { g -> if (g.status == event.status) g.copy(collapsed = g.status in collapsed) else g })
                }
            }
        }
    }

    private suspend fun reload() {
        try {
            val today = container.clock().toLocalDate()
            val (hasGroup, groups) = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                if (gid.isEmpty()) return@withContext false to emptyList<HomeworkGroupUi>()
                val lessons = container.repo.allForGroup(gid)
                val items = container.homework.all().map { hw ->
                    val lesson = lessons.firstOrNull { it.subjectNormalized == hw.norm }
                    val subject = if (lesson != null) {
                        container.overrides.displayNameByNorm(hw.norm, lesson.dayOfWeek).ifBlank {
                            LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
                        }
                    } else hw.norm
                    HomeworkGroups.toItem(hw, subject, today, container.copy, lesson?.subjectRaw.orEmpty())
                }
                true to HomeworkGroups.group(items, container.copy).map { it.copy(collapsed = it.status in collapsed) }
            }
            mutable.update { it.copy(loaded = true, hasGroup = hasGroup, groups = groups) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaHomework", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    private fun openPicker() {
        viewModelScope.launch {
            val subjects = withContext(Dispatchers.IO) {
                val gid = container.repo.settings().myGroupId.orEmpty()
                container.repo.allForGroup(gid).distinctBy { it.subjectNormalized }.map {
                    SubjectUi(
                        raw = it.subjectRaw,
                        norm = it.subjectNormalized,
                        display = container.overrides.displayNameByNorm(it.subjectNormalized, it.dayOfWeek)
                            .ifBlank { LessonFormat.stripType(it.subjectRaw, it.typeRaw) }
                    )
                }.sortedBy { it.display }
            }
            mutable.update { it.copy(subjectPicker = SubjectPickerUi(subjects)) }
        }
    }

    private fun openNew(raw: String) {
        val display = mutable.value.subjectPicker?.subjects?.firstOrNull { it.raw == raw }?.display
            ?: LessonFormat.stripType(raw, "")
        mutable.update {
            it.copy(subjectPicker = null, editor = editorState(null, raw, display, "", 1, false))
        }
    }

    private fun openEdit(id: Long) {
        viewModelScope.launch {
            val item = mutable.value.groups.flatMap { it.items }.firstOrNull { it.id == id } ?: return@launch
            mutable.update {
                it.copy(editor = editorState(id, item.subjectRaw.ifBlank { item.subject }, item.subject, item.text, item.n, true))
            }
        }
    }

    private fun editorState(id: Long?, raw: String, display: String, text: String, n: Int, edit: Boolean): HomeworkEditorState {
        val norm = Parity.normalizeSubject(raw)
        return HomeworkEditorState(id, raw, display, text, n, edit) { target ->
            val prefs = container.repo.settings()
            val gid = prefs.myGroupId.orEmpty()
            val c = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            val all = container.repo.allForGroup(gid)
            container.homework.dueDateIn(
                { g, dow, parity -> all.filter { it.groupId == g && it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) } },
                c, norm, LocalDate.now(), target
            )
        }
    }

    private fun save() {
        val editor = mutable.value.editor ?: return
        if (!editor.canSave) return
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                if (editor.id == null) container.homework.addHomework(editor.subjectRaw, editor.text.trim(), editor.n)
                else container.homework.updateHomework(editor.id, editor.text.trim(), editor.n)
            }
            mutable.update { it.copy(editor = null) }
            container.toasts.show(container.app.getString(R.string.hw_saved), ToastKind.Ok)
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = HomeworkViewModel(container) as T
        }
    }
}
