package ru.bgtu_voenmeh.zapara.ui.homework

import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
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
import ru.bgtu_voenmeh.zapara.data.HomeworkFileException
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import java.time.LocalDate

class HomeworkViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(HomeworkUiState(guest = container.profile.isGuest))
    val state: StateFlow<HomeworkUiState> = mutable.asStateFlow()
    private val writes = Mutex()
    private var saving = false
    private var reloadTicket = 0
    private var collapsed = setOf(GroupStatus.Done)

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: HomeworkEvent) {
        when (event) {
            is HomeworkEvent.ToggleDone -> viewModelScope.launch {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        val hw = container.homework.all().firstOrNull { it.id == event.id } ?: return@withContext
                        container.homework.markDone(event.id, !hw.done)
                    }
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
            is HomeworkEvent.EditorShare -> mutable.update { s -> s.copy(editor = s.editor?.copy(share = event.on)) }
            HomeworkEvent.Inc -> mutable.update { s -> s.copy(editor = s.editor?.inc()) }
            HomeworkEvent.Dec -> mutable.update { s -> s.copy(editor = s.editor?.dec()) }
            HomeworkEvent.Save -> save()
            HomeworkEvent.Cancel -> cancelEditor()
            is HomeworkEvent.Attach -> attach(event.kind, event.uri)
            is HomeworkEvent.RemoveFile -> removeFile(event.id)
            is HomeworkEvent.OpenFile -> openFile(event.homeworkId, event.fileId)
            is HomeworkEvent.AskDelete -> mutable.update { it.copy(confirmDelete = event.id) }
            HomeworkEvent.ConfirmDelete -> {
                val id = mutable.value.confirmDelete ?: return
                mutable.update { it.copy(confirmDelete = null) }
                viewModelScope.launch {
                    writes.withLock { withContext(Dispatchers.IO) {
                        container.homework.delete(id)
                        container.homeworkFiles.deleteHomework(id)
                    } }
                    container.events.emit(AppEvent.PersonalizationChanged)
                }
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
        val ticket = ++reloadTicket
        try {
            val today = container.clock().toLocalDate()
            val (hasGroup, groups) = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                if (gid.isEmpty()) return@withContext false to emptyList<HomeworkGroupUi>()
                val lessons = container.ownLessons()
                val items = container.homework.all().map { hw ->
                    val lesson = lessons.firstOrNull { Parity.sameSubject(it.subjectNormalized, hw.norm) }
                    val subject = if (lesson != null) {
                        container.overrides.displayNameByNorm(hw.norm, lesson.dayOfWeek).ifBlank {
                            LessonFormat.stripType(lesson.subjectRaw, lesson.typeRaw)
                        }
                    } else hw.norm
                    HomeworkGroups.toItem(hw, subject, today, container.copy, lesson?.subjectRaw.orEmpty())
                        .copy(files = container.homeworkFiles.list(hw.id))
                }
                true to HomeworkGroups.group(items, container.copy)
            }
            if (ticket != reloadTicket) return
            mutable.update {
                it.copy(
                    loaded = true,
                    hasGroup = hasGroup,
                    groups = groups.map { group -> group.copy(collapsed = group.status in collapsed) }
                )
            }
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
                container.ownLessons().distinctBy { it.subjectNormalized }.map {
                    SubjectUi(
                        raw = it.subjectRaw,
                        norm = it.subjectNormalized,
                        display = container.overrides.displayNameByNorm(it.subjectNormalized, it.dayOfWeek)
                            .ifBlank { LessonFormat.stripType(it.subjectRaw, it.typeRaw) },
                        type = it.typeRaw
                    )
                }.sortedBy { it.display }
            }
            mutable.update { it.copy(subjectPicker = SubjectPickerUi(subjects)) }
        }
    }

    private fun openNew(raw: String) {
        val display = mutable.value.subjectPicker?.subjects?.firstOrNull { it.raw == raw }?.display
            ?: LessonFormat.stripType(raw, "")
        viewModelScope.launch { showEditor(null, raw, display, "", 1, false, closePicker = true) }
    }

    private fun openEdit(id: Long) {
        viewModelScope.launch {
            val item = mutable.value.groups.flatMap { it.items }.firstOrNull { it.id == id } ?: return@launch
            showEditor(id, item.subjectRaw.ifBlank { item.subject }, item.subject, item.text, item.n, true, closePicker = false)
        }
    }

    private suspend fun showEditor(
        id: Long?, raw: String, display: String, text: String, n: Int, edit: Boolean, closePicker: Boolean
    ) {
        val prepared = withContext(Dispatchers.IO) {
            snapshotDue(raw, id) to (if (id == null) emptyList() else container.homeworkFiles.list(id))
        }
        mutable.update {
            it.copy(
                subjectPicker = if (closePicker) null else it.subjectPicker,
                editor = HomeworkEditorState(
                    id, raw, display, text, n, edit, prepared.first,
                    files = prepared.second,
                    draft = java.util.UUID.randomUUID().toString()
                )
            )
        }
    }

    private fun snapshotDue(raw: String, id: Long?): (Int, String) -> LocalDate? {
        val prefs = container.repo.settings()
        val gid = prefs.myGroupId.orEmpty()
        val c = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
        val all = container.ownLessons()
        val today = container.clock().toLocalDate()
        val norm = Parity.normalizeSubject(raw)
        val existing = id?.let(container.homework::getById)
        return homeworkEditorDueFor(existing, today) { from, target ->
            container.homework.dueDateIn(
                { g, dow, parity -> all.filter { it.groupId == g && it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) } },
                c, existing?.norm ?: norm, from, target
            )
        }
    }

    private fun save() {
        if (saving) return
        val editor = mutable.value.editor ?: return
        if (!editor.canSave) return
        saving = true
        mutable.update { it.copy(editor = null) }
        viewModelScope.launch {
            try {
                val outcome = writes.withLock {
                    withContext(Dispatchers.IO) {
                        shareSavedHomework(container, editor) {
                            if (editor.id == null) {
                                val id = container.homework.addHomework(editor.subjectRaw, editor.text.trim(), editor.n)
                                if (editor.draft.isNotEmpty()) container.homeworkFiles.commit(editor.draft, id, editor.removed)
                            } else {
                                val existing = container.homework.getById(editor.id)
                                if (existing != null) {
                                    if (editor.hasChanges(existing)) container.homework.updateHomework(editor.id, editor.text.trim(), editor.n)
                                    if (editor.draft.isNotEmpty()) container.homeworkFiles.commit(editor.draft, editor.id, editor.removed)
                                }
                            }
                        }
                    }
                }
                val note = outcome.note.ifBlank { container.app.getString(R.string.hw_saved) }
                container.toasts.show(note, ToastKind.Ok)
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                mutable.update { cur -> if (cur.editor == null) cur.copy(editor = editor) else cur }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "save", e)
                mutable.update { cur -> if (cur.editor == null) cur.copy(editor = editor) else cur }
                container.toasts.show(container.app.getString(R.string.homework_save_failed), ToastKind.Bad)
            } finally {
                saving = false
            }
        }
    }

    private fun cancelEditor() {
        val editor = mutable.value.editor
        mutable.update { it.copy(editor = null) }
        if (editor != null && editor.draft.isNotEmpty()) {
            viewModelScope.launch(Dispatchers.IO) { container.homeworkFiles.discard(editor.draft) }
        }
    }

    private fun attach(kind: String, uri: Uri) {
        val editor = mutable.value.editor ?: return
        if (editor.draft.isEmpty()) return
        viewModelScope.launch {
            try {
                val saved = withContext(Dispatchers.IO) {
                    container.homeworkFiles.importUri(container.app, uri, editor.draft, kind, editor.files.count { !it.staged })
                }
                mutable.update { state ->
                    val current = state.editor?.takeIf { it.draft == editor.draft } ?: return@update state
                    state.copy(editor = current.copy(files = current.files + saved))
                }
            } catch (e: HomeworkFileException) {
                container.toasts.show(container.app.getString(fileMessage(e.code)), ToastKind.Bad)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "attach", e)
                container.toasts.show(container.app.getString(R.string.hw_attach_bad), ToastKind.Bad)
            }
        }
    }

    private fun removeFile(id: String) {
        val editor = mutable.value.editor ?: return
        val file = editor.files.firstOrNull { it.id == id } ?: return
        mutable.update { state ->
            val current = state.editor ?: return@update state
            state.copy(editor = current.copy(
                files = current.files.filter { it.id != id },
                removed = current.removed + id
            ))
        }
        if (file.staged && editor.draft.isNotEmpty()) {
            viewModelScope.launch(Dispatchers.IO) { container.homeworkFiles.discardFile(editor.draft, id) }
        }
    }

    private fun openFile(homeworkId: Long, fileId: String) {
        val located = container.homeworkFiles.savedFile(homeworkId, fileId) ?: return
        val uri = FileProvider.getUriForFile(container.app, container.app.packageName + ".fileprovider", located.first)
        val view = Intent(Intent.ACTION_VIEW).setDataAndType(uri, located.second).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        if (runCatching { container.app.startActivity(view) }.isFailure)
            container.toasts.show(container.app.getString(R.string.hw_attach_bad), ToastKind.Bad)
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = HomeworkViewModel(container) as T
        }
    }
}

internal fun fileMessage(code: String): Int = when (code) {
    "big" -> R.string.hw_attach_big
    "full" -> R.string.hw_attach_full
    else -> R.string.hw_attach_bad
}
