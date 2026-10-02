package ru.bgtu_voenmeh.zapara.ui.homework

import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.HomeworkFileException
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.communities.SharedHomeworkCache
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

class HomeworkViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(HomeworkUiState(guest = container.profile.isGuest))
    val state: StateFlow<HomeworkUiState> = mutable.asStateFlow()
    private val writes = Mutex()
    private var saving = false
    private var reloadTicket = 0
    private var collapsed = setOf(GroupStatus.Done)
    private var currentGroupId: String? = null
    private var undoSerial = 0L
    private val sharedCache = SharedHomeworkCache()
    private var sharedTicket = 0L
    private var groupEpoch = 0L
    private var pickerTicket = 0L
    private data class SharedToggleKey(val profile: String, val group: String, val community: String, val id: String)
    private val sharedToggleRequests = RequestTokens<SharedToggleKey>()
    private data class PersonalToggleKey(val profile: String, val group: String, val id: Long)
    private val personalToggleRequests = RequestTokens<PersonalToggleKey>()

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: HomeworkEvent) {
        when (event) {
            is HomeworkEvent.PreviewPublication -> previewPublication(event.ids)
            is HomeworkEvent.PublicationAudience -> mutable.update { state ->
                val batch = state.publication
                if (batch == null || batch.busy || batch.locked) state else state.copy(publication = batch.copy(audience = event.audience))
            }
            HomeworkEvent.ConfirmPublication -> publishPersonalBatch()
            HomeworkEvent.DiscardPublication -> if (mutable.value.publication?.busy != true) mutable.update { it.copy(publication = null) }
            HomeworkEvent.ResumePublication -> mutable.update { it.copy(publication = it.publication?.copy(open = true)) }
            HomeworkEvent.ClosePublication -> mutable.update { state ->
                val batch = state.publication
                if (batch?.busy == true) state else state.copy(publication = batch?.takeIf { it.locked && it.rows.any { row -> !row.sent } }?.copy(open = false))
            }
            is HomeworkEvent.PreviewPostpone -> previewPostpone(event.ids)
            HomeworkEvent.ConfirmPostpone -> applyPostpone(false)
            HomeworkEvent.UndoPostpone -> applyPostpone(true)
            HomeworkEvent.ClosePostpone -> if (mutable.value.reschedule?.busy != true) mutable.update { it.copy(reschedule = null) }
            is HomeworkEvent.ToggleDone -> toggleDone(event.id)
            HomeworkEvent.UndoDone -> undoDone()
            is HomeworkEvent.Edit -> openEdit(event.id)
            HomeworkEvent.Add -> openPicker()
            is HomeworkEvent.Clone -> openClone(event.id)
            is HomeworkEvent.BulkDone -> completeMany(event.ids)
            HomeworkEvent.UndoBulkDone -> undoMany()
            HomeworkEvent.RetryLoad -> viewModelScope.launch { reload() }
            HomeworkEvent.RetryShared -> currentGroupId?.let { group -> viewModelScope.launch { refreshShared(group) } }
            is HomeworkEvent.ToggleShared -> toggleShared(event.id)
            is HomeworkEvent.BrowseQuery -> mutable.update { it.copy(browseQuery = event.value) }
            is HomeworkEvent.DeadlineFilter -> mutable.update { it.copy(deadlineFilter = event.value,
                originFilter = if (event.value == HomeworkDeadlineFilter.All) it.originFilter else HomeworkOriginFilter.Personal) }
            is HomeworkEvent.OriginFilter -> mutable.update { it.copy(originFilter = event.value,
                deadlineFilter = if (event.value == HomeworkOriginFilter.Shared) HomeworkDeadlineFilter.All else it.deadlineFilter,
                withFilesOnly = if (event.value == HomeworkOriginFilter.Shared) false else it.withFilesOnly) }
            is HomeworkEvent.WithFilesOnly -> mutable.update { it.copy(withFilesOnly = event.value,
                originFilter = if (event.value) HomeworkOriginFilter.Personal else it.originFilter) }
            is HomeworkEvent.SortBySubject -> mutable.update { it.copy(sortBySubject = event.value) }
            is HomeworkEvent.BrowseFilter -> {
                if (event.value == HomeworkCompletionFilter.Done) collapsed = collapsed - GroupStatus.Done
                mutable.update { s -> s.copy(browseFilter = event.value,
                    groups = s.groups.map { it.copy(collapsed = it.status in collapsed) }) }
            }
            HomeworkEvent.BrowseReset -> mutable.update { it.resetBrowse() }
            is HomeworkEvent.Query -> mutable.update { s ->
                s.copy(subjectPicker = s.subjectPicker?.copy(query = event.value))
            }
            is HomeworkEvent.PickSubject -> openNew(event.raw)
            is HomeworkEvent.PickManualSubject -> {
                val picker = mutable.value.subjectPicker
                if (picker != null && picker.matches(currentGroupId, container.profile.databaseName, groupEpoch) &&
                    manualSubjectAllowed(picker.subjects, event.raw) && currentGroupId?.isNotBlank() == true)
                    openNew(event.raw.trim())
            }
            HomeworkEvent.ClosePicker -> { ++pickerTicket; mutable.update { it.copy(subjectPicker = null) } }
            is HomeworkEvent.EditorText -> mutable.update { s -> s.copy(editor = s.editor?.withText(event.text)) }
            is HomeworkEvent.EditorShare -> {
                mutable.update { s -> s.copy(editor = s.editor?.withShare(event.on)) }
                if (event.on) loadShareOptions()
            }
            is HomeworkEvent.EditorAudience -> mutable.update { s -> s.copy(editor = s.editor?.withAudience(event.audience)) }
            HomeworkEvent.RetryShareOptions -> loadShareOptions()
            HomeworkEvent.RetryShare -> retryShare()
            HomeworkEvent.Inc -> mutable.update { s -> s.copy(editor = s.editor?.inc()) }
            HomeworkEvent.Dec -> mutable.update { s -> s.copy(editor = s.editor?.dec()) }
            HomeworkEvent.Recalculate -> viewModelScope.launch {
                val editor=mutable.value.editor ?: return@launch
                if (editor.busy) return@launch
                mutable.update { it.copy(editor = editor.copy(work = HomeworkEditorWork.Recalculating, error = null)) }
                try {
                    val prepared=withContext(Dispatchers.IO) { snapshotDue(editor.subjectRaw,editor.persistedId ?: editor.id,editor.creationAnchor(container.clock().toLocalDate())) to container.repo.settings().myGroupId }
                    mutable.update { state -> if(state.editor?.draft==editor.draft) state.copy(editor=state.editor.copy(dueFor=prepared.first,scheduleGroupId=prepared.second,sourceChanged=false)) else state }
                } catch (e: CancellationException) { throw e }
                catch (_: Exception) { editorError(editor.draft, container.app.getString(R.string.review_homework_source_changed)) }
                finally { finishEditorWork(editor.draft) }
            }
            HomeworkEvent.Save -> save()
            HomeworkEvent.ApproveDuplicate -> {
                mutable.update { it.copy(editor = it.editor?.copy(duplicateApproved = true,
                    duplicateWarning = false)) }
                save()
            }
            HomeworkEvent.CancelDuplicate -> mutable.update { it.copy(editor =
                it.editor?.copy(duplicateWarning = false)) }
            HomeworkEvent.Cancel -> cancelEditor()
            is HomeworkEvent.Attach -> attach(event.kind, event.uri)
            is HomeworkEvent.AttachMany -> attachMany(event.kind, event.uris)
            is HomeworkEvent.RemoveFile -> removeFile(event.id)
            is HomeworkEvent.OpenFile -> openFile(event.homeworkId, event.fileId)
            is HomeworkEvent.AskDelete -> mutable.update { it.copy(confirmDelete = event.id, deleteError = null) }
            HomeworkEvent.ConfirmDelete -> confirmDelete()
            HomeworkEvent.CancelDelete -> if (!mutable.value.deleteBusy)
                mutable.update { it.copy(confirmDelete = null, deleteError = null) }
            is HomeworkEvent.ToggleGroup -> {
                collapsed = if (event.status in collapsed) collapsed - event.status else collapsed + event.status
                mutable.update { s ->
                    s.copy(groups = s.groups.map { g -> if (g.status == event.status) g.copy(collapsed = g.status in collapsed) else g })
                }
            }
            HomeworkEvent.ExpandGroups -> {
                collapsed = emptySet()
                mutable.update { s -> s.copy(groups = s.groups.map { it.copy(collapsed = false) }) }
            }
            HomeworkEvent.CollapseGroups -> {
                collapsed = GroupStatus.entries.toSet()
                mutable.update { s -> s.copy(groups = s.groups.map { it.copy(collapsed = true) }) }
            }
        }
    }

    private suspend fun reload() {
        val ticket = ++reloadTicket
        var attemptedGroup: String? = null
        try {
            val today = container.clock().toLocalDate()
            val (groupId, groups) = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                attemptedGroup = gid
                if (gid.isEmpty()) return@withContext gid to emptyList<HomeworkGroupUi>()
                val lessons = container.ownLessons()
                if (lessons.isNotEmpty()) container.homework.recomputeAll(today)
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
                gid to HomeworkGroups.group(items, container.copy)
            }
            if (ticket != reloadTicket) return
            val changedGroup = currentGroupId != null && currentGroupId != groupId
            currentGroupId = groupId
            if (changedGroup) ++undoSerial
            if (changedGroup) {
                ++groupEpoch; ++pickerTicket
                sharedToggleRequests.clear()
                personalToggleRequests.clear()
                ++sharedTicket
                sharedCache.clear()
            }
            mutable.update {
                val state = if (changedGroup) it.forGroupChange() else it
                state.copy(
                    loaded = true,
                    groupId = groupId, profileName = container.profile.databaseName,
                    hasGroup = groupId.isNotEmpty(),
                    groups = groups.map { group -> group.copy(collapsed = group.status in collapsed) },
                    loadError = null,
                    browseQuery = state.browseQuery,
                    browseFilter = state.browseFilter,
                    undoDone = state.undoDone,
                    sharedRows = if (changedGroup) emptyList() else state.sharedRows,
                    sharedLoading = if (changedGroup) false else state.sharedLoading,
                    sharedError = if (changedGroup) null else state.sharedError,
                    sharedBusyIds = if (changedGroup) emptySet() else state.sharedBusyIds,
                    personalBusyIds = if (changedGroup) emptySet() else state.personalBusyIds,
                    subjectPicker = if (changedGroup) null else state.subjectPicker
                )
            }
            viewModelScope.launch { refreshShared(groupId) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaHomework", "reload", e)
            if (ticket != reloadTicket) return
            val changedGroup = attemptedGroup != null && attemptedGroup != currentGroupId
            if (changedGroup) {
                currentGroupId = attemptedGroup
                ++groupEpoch; ++pickerTicket; ++undoSerial; ++sharedTicket
                sharedToggleRequests.clear()
                personalToggleRequests.clear()
                sharedCache.clear()
            }
            mutable.update { state ->
                val retained = if (changedGroup) state.forGroupChange().copy(
                    hasGroup = !attemptedGroup.isNullOrEmpty(), groups = emptyList(), sharedRows = emptyList(),
                    sharedLoading = false, sharedError = null, sharedBusyIds = emptySet(),
                    personalBusyIds = emptySet(), subjectPicker = null) else state
                retained.copy(loaded = true, loadError = container.app.getString(R.string.uxnext_homework_load_failed))
            }
        }
    }

    private fun confirmDelete() {
        val id = mutable.value.confirmDelete ?: return
        if (mutable.value.deleteBusy) return
        val group = currentGroupId ?: return
        mutable.update { it.copy(deleteBusy = true, deleteError = null) }
        viewModelScope.launch {
            try {
                val deleted = writes.withLock { withContext(Dispatchers.IO) {
                    val activeContainer = (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container
                    if (activeContainer != null && activeContainer !== container) return@withContext false
                    if (container.repo.settings().myGroupId.orEmpty() != group || currentGroupId != group ||
                        container.homework.getById(id) == null) return@withContext false
                    container.homework.delete(id)
                    true
                } }
                if (!deleted) {
                    mutable.update { it.copy(confirmDelete = null, deleteError = null) }
                    container.toasts.show(container.app.getString(R.string.homework_widget_unavailable), ToastKind.Bad)
                    return@launch
                }
                ++undoSerial
                mutable.update { it.copy(confirmDelete = null, undoDone = null, deleteError = null) }
                container.events.emit(AppEvent.PersonalizationChanged)
                try { withContext(Dispatchers.IO) { container.homeworkFiles.deleteHomework(id) } }
                catch (e: CancellationException) { throw e }
                catch (e: Exception) {
                    android.util.Log.w("ZaparaHomework", "delete files", e)
                    container.toasts.show(container.app.getString(R.string.uxnext_homework_files_cleanup_failed), ToastKind.Bad)
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "delete", e)
                mutable.update { state -> if (state.confirmDelete == id)
                    state.copy(deleteError = container.app.getString(R.string.uxnext_homework_delete_failed)) else state }
            } finally { mutable.update { it.copy(deleteBusy = false) } }
        }
    }

    private fun sharedScopeCurrent(group: String, profile: String): Boolean =
        currentGroupId == group && container.profile.databaseName == profile &&
            (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container
                ?.let { it === container } != false

    private suspend fun refreshShared(group: String) {
        val ticket = ++sharedTicket
        val profile = container.profile.databaseName
        val api = container.communities
        if (group.isBlank() || container.profile.isGuest || api == null) {
            sharedCache.clear()
            mutable.update { it.copy(sharedRows = emptyList(), sharedLoading = false, sharedError = null) }
            return
        }
        mutable.update { it.copy(sharedLoading = true, sharedError = null) }
        try {
            val token = withContext(Dispatchers.IO) { container.accessToken() }
            val result = sharedCache.refresh(api, token, group)
            if (ticket != sharedTicket || !sharedScopeCurrent(group, profile) || !result.applied) return
            val deadline = DateTimeFormatter.ofPattern("d MMMM", Locale("ru"))
            val rows = result.snapshot?.rows.orEmpty().map { row ->
                val completion = result.snapshot?.completions?.get(row.homeworkId)
                SharedHomeworkItemUi(row.homeworkId, row.communityId, row.title, row.body,
                    row.deadlineAt?.atZone(ZoneId.systemDefault())?.toLocalDate()?.format(deadline)
                        ?: container.app.getString(R.string.uxnext_homework_no_deadline),
                    completion?.completed == true, completion?.revision ?: 0,
                    row.canComplete, row.audience?.selected == true)
            }
            mutable.update { it.copy(sharedRows = rows, sharedLoading = false,
                sharedError = when {
                    token == null -> container.app.getString(R.string.uxnext_homework_shared_auth)
                    result.failed -> container.app.getString(R.string.uxnext_homework_shared_failed)
                    else -> null
                }) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaHomework", "shared reload", e)
            if (ticket == sharedTicket && sharedScopeCurrent(group, profile)) mutable.update {
                it.copy(sharedLoading = false, sharedError = container.app.getString(R.string.uxnext_homework_shared_failed))
            }
        }
    }

    private fun toggleShared(id: String) {
        val current = mutable.value.sharedRows.firstOrNull { it.id == id && it.canComplete } ?: return
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        val key = SharedToggleKey(profile, group, current.communityId, id)
        val serial = sharedToggleRequests.begin(key) ?: return
        ++sharedTicket // An older refresh cannot replace a completion accepted below.
        mutable.update { it.copy(sharedBusyIds = it.sharedBusyIds + id,
            sharedLoading = false, sharedError = null) }
        viewModelScope.launch {
            try {
                val outcome = withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != group ||
                        !sharedScopeCurrent(group, profile) || epoch != groupEpoch) return@withContext null
                    val token = container.accessToken() ?: return@withContext null
                    val result = container.communities?.upsertCompletion(token, current.communityId, id,
                        !current.completed, current.completionRevision) ?: return@withContext null
                    token to result
                }
                if (outcome == null || !sharedScopeCurrent(group, profile) || epoch != groupEpoch ||
                    !sharedToggleRequests.current(key, serial)) return@launch
                val (token, result) = outcome
                sharedCache.acknowledgeCompletion(group, current.communityId, token, result)
                mutable.update { state -> state.copy(sharedRows = state.sharedRows.map { row ->
                    if (row.id == id && row.communityId == current.communityId) row.copy(
                        completed = result.completed, completionRevision = result.revision) else row
                }) }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "shared completion", e)
                if (sharedScopeCurrent(group, profile) && epoch == groupEpoch &&
                    sharedToggleRequests.current(key, serial)) mutable.update {
                    it.copy(sharedError = container.app.getString(R.string.uxnext_homework_shared_save_failed))
                }
            } finally {
                if (sharedToggleRequests.finish(key, serial)) {
                    if (epoch == groupEpoch && sharedScopeCurrent(group, profile))
                        mutable.update { it.copy(sharedBusyIds = it.sharedBusyIds - id) }
                }
            }
        }
    }

    private fun openPicker() {
        val requestedGroup = currentGroupId ?: return
        val requestedProfile = container.profile.databaseName
        val epoch = groupEpoch
        val ticket = ++pickerTicket
        viewModelScope.launch {
            try {
                val (gid, subjects) = withContext(Dispatchers.IO) {
                    val gid = container.repo.settings().myGroupId.orEmpty()
                    gid to container.ownLessons().distinctBy { it.subjectNormalized }.map {
                    SubjectUi(
                        raw = it.subjectRaw,
                        norm = it.subjectNormalized,
                        display = container.overrides.displayNameByNorm(it.subjectNormalized, it.dayOfWeek)
                            .ifBlank { LessonFormat.stripType(it.subjectRaw, it.typeRaw) },
                        type = it.typeRaw
                    )
                    }.sortedBy { it.display }
                }
                if (ticket != pickerTicket || epoch != groupEpoch || requestedGroup != gid ||
                    requestedGroup != currentGroupId || requestedProfile != container.profile.databaseName) return@launch
                mutable.update { it.copy(subjectPicker = SubjectPickerUi(subjects,
                    groupId = gid, profileName = requestedProfile, groupEpoch = epoch)) }
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) {
                if (ticket == pickerTicket && epoch == groupEpoch)
                    container.toasts.show(container.app.getString(R.string.ux60_picker_failed), ToastKind.Bad)
            }
        }
    }

    private fun openNew(raw: String) {
        val picker = mutable.value.subjectPicker ?: return
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        if (!picker.matches(group, profile, epoch) ||
            picker.subjects.none { it.raw == raw } && !manualSubjectAllowed(picker.subjects, raw)) return
        val display = picker.subjects.firstOrNull { it.raw == raw }?.display
            ?: LessonFormat.stripType(raw, "")
        ++pickerTicket
        viewModelScope.launch { showEditor(null, raw, display, "", 1, false, closePicker = true,
            expectedGroup = group, expectedProfile = profile, expectedEpoch = epoch) }
    }

    private fun openEdit(id: Long) {
        viewModelScope.launch {
            val activeEditor = mutable.value.editor
            when (homeworkEditDecision(activeEditor, id)) {
                HomeworkEditDecision.AlreadyOpen -> return@launch
                HomeworkEditDecision.Blocked -> {
                    container.toasts.show(container.app.getString(R.string.homework_widget_editor_busy), ToastKind.Bad)
                    return@launch
                }
                HomeworkEditDecision.Open, HomeworkEditDecision.ReplacePristine -> Unit
            }
            val item = mutable.value.groups.flatMap { it.items }.firstOrNull { it.id == id }
            val expectedGroupId = currentGroupId
            val expectedEpoch = groupEpoch
            val expectedProfile = container.profile.databaseName
            val exists = withContext(Dispatchers.IO) {
                container.repo.settings().myGroupId.orEmpty() == expectedGroupId && container.homework.getById(id) != null
            }
            if (item == null || !exists || currentGroupId != expectedGroupId || groupEpoch != expectedEpoch ||
                container.profile.databaseName != expectedProfile) {
                container.toasts.show(container.app.getString(R.string.homework_widget_unavailable), ToastKind.Bad)
                return@launch
            }
            if (!homeworkEditStillAllowed(activeEditor, mutable.value.editor, id)) {
                container.toasts.show(container.app.getString(R.string.homework_widget_editor_busy), ToastKind.Bad)
                return@launch
            }
            if (activeEditor != null) cancelEditor()
            showEditor(id, item.subjectRaw.ifBlank { item.subject }, item.subject, item.text, item.n, true,
                closePicker = false, expectedGroup = expectedGroupId.orEmpty(), expectedProfile = expectedProfile,
                expectedEpoch = expectedEpoch)
        }
    }

    private fun openClone(id: Long) {
        if (mutable.value.editor != null) {
            container.toasts.show(container.app.getString(R.string.homework_widget_editor_busy), ToastKind.Bad)
            return
        }
        val item = mutable.value.groups.flatMap { it.items }.firstOrNull { it.id == id && it.done } ?: return
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        viewModelScope.launch {
            val exists = withContext(Dispatchers.IO) {
                container.repo.settings().myGroupId.orEmpty() == group &&
                    container.homework.getById(id)?.done == true
            }
            if (!exists || mutable.value.editor != null || currentGroupId != group ||
                container.profile.databaseName != profile || groupEpoch != epoch) return@launch
            showEditor(null, item.subjectRaw.ifBlank { item.subject }, item.subject,
                item.text, item.n, false, closePicker = false,
                expectedGroup = group, expectedProfile = profile, expectedEpoch = epoch)
        }
    }

    private suspend fun showEditor(
        id: Long?, raw: String, display: String, text: String, n: Int, edit: Boolean, closePicker: Boolean,
        expectedGroup: String = currentGroupId.orEmpty(), expectedProfile: String = container.profile.databaseName,
        expectedEpoch: Long = groupEpoch
    ) {
        if (mutable.value.editor != null || expectedGroup != currentGroupId ||
            expectedProfile != container.profile.databaseName || expectedEpoch != groupEpoch) return
        val anchor = container.clock().toLocalDate()
        val prepared = withContext(Dispatchers.IO) {
            Triple(snapshotDue(raw, id, anchor), loadEditorFiles(container, id),
                container.repo.settings().myGroupId)
        }
        if (mutable.value.editor != null || expectedGroup != currentGroupId ||
            expectedProfile != container.profile.databaseName || expectedEpoch != groupEpoch ||
            prepared.third != expectedGroup) return
        mutable.update {
            it.copy(
                subjectPicker = if (closePicker) null else it.subjectPicker,
                editor = HomeworkEditorState(
                    id, raw, display, text, n, edit, prepared.first,
                    files = prepared.second.first, missingFileIds = prepared.second.second,
                    draft = java.util.UUID.randomUUID().toString(), anchorDate = anchor, scheduleGroupId = prepared.third
                )
            )
        }
    }

    private fun snapshotDue(raw: String, id: Long?, anchor: LocalDate = container.clock().toLocalDate()): (Int, String) -> LocalDate? {
        val prefs = container.repo.settings()
        val gid = prefs.myGroupId.orEmpty()
        val c = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
        val all = container.ownLessons()
        val today = anchor
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
        mutable.update { it.copy(editor = editor.copy(work = HomeworkEditorWork.Saving, error = null)) }
        viewModelScope.launch {
            try {
                val outcome = writes.withLock {
                    withContext(Dispatchers.IO) {
                        saveHomeworkEditor(container, editor) { progress ->
                            mutable.update { current -> if (current.editor?.draft == editor.draft)
                                current.copy(editor = progress.copy(work = HomeworkEditorWork.Saving)) else current }
                        }
                    }
                }
                mutable.update { current -> if (current.editor?.draft != editor.draft) current else if (!outcome.sent && current.editor.shareRequest != null)
                    current.copy(editor = current.editor.copy(error = outcome.note)) else current.copy(editor = null) }
                val note = outcome.note.ifBlank { container.app.getString(R.string.hw_saved) }
                container.toasts.show(note, if (editor.share && !outcome.sent) ToastKind.Bad else ToastKind.Ok)
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                throw e
            } catch (_: DuplicateHomework) {
                mutable.update { current -> if (current.editor?.draft == editor.draft)
                    current.copy(editor = current.editor.copy(duplicateWarning = true, error = null))
                    else current }
            } catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "save", e)
                val message = container.app.getString(if (e is HomeworkScheduleChanged) R.string.review_homework_source_changed
                    else if (mutable.value.editor?.persistedId != null) R.string.homework_ux_partial_save_failed else R.string.homework_ux_save_failed)
                mutable.update { cur -> if (cur.editor?.draft == editor.draft) cur.copy(editor = cur.editor.copy(error = message,
                    sourceChanged = cur.editor.sourceChanged || e is HomeworkScheduleChanged)) else cur }
            } finally {
                saving = false
                finishEditorWork(editor.draft)
            }
        }
    }

    private fun cancelEditor() {
        val editor = mutable.value.editor
        if (editor?.busy == true) return
        mutable.update { it.copy(editor = null) }
        if (editor != null && editor.draft.isNotEmpty()) {
            viewModelScope.launch(Dispatchers.IO) { container.homeworkFiles.discard(editor.draft) }
        }
    }

    private fun attach(kind: String, uri: Uri) {
        val editor = mutable.value.editor ?: return
        if (editor.draft.isEmpty() || editor.busy) return
        mutable.update { it.copy(editor = editor.copy(work = HomeworkEditorWork.Attachment, error = null)) }
        viewModelScope.launch {
            var imported: ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile? = null
            var adopted = false
            try {
                val saved = writes.withLock { withContext(Dispatchers.IO) {
                    container.homeworkFiles.importUri(container.app, uri, editor.draft, kind, editor.files.count { !it.staged }).also { imported = it }
                } }
                mutable.update { state ->
                    val current = state.editor?.takeIf { it.draft == editor.draft } ?: return@update state
                    state.copy(editor = current.copy(files = current.files + saved))
                }
                adopted = mutable.value.editor?.let { it.draft == editor.draft && it.files.any { file -> file.id == saved.id } } == true
            } catch (e: HomeworkFileException) {
                editorError(editor.draft, container.app.getString(fileMessage(e.code)))
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "attach", e)
                editorError(editor.draft, container.app.getString(R.string.hw_attach_bad))
            } finally {
                if (!adopted) imported?.let { file -> withContext(NonCancellable + Dispatchers.IO) {
                    runCatching { container.homeworkFiles.discardFile(editor.draft, file.id) }
                } }
                finishEditorWork(editor.draft)
            }
        }
    }

    private fun removeFile(id: String) {
        val editor = mutable.value.editor ?: return
        if (editor.busy) return
        val file = editor.files.firstOrNull { it.id == id } ?: return
        mutable.update { it.copy(editor = editor.copy(work = HomeworkEditorWork.Attachment, error = null)) }
        viewModelScope.launch {
            try {
                if (file.staged && editor.draft.isNotEmpty()) writes.withLock { withContext(Dispatchers.IO) { container.homeworkFiles.discardFile(editor.draft, id) } }
                mutable.update { state ->
                    val current = state.editor?.takeIf { it.draft == editor.draft } ?: return@update state
                    state.copy(editor = current.copy(files = current.files.filter { it.id != id },
                        missingFileIds = current.missingFileIds - id, removed = current.removed + id))
                }
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) { editorError(editor.draft, container.app.getString(R.string.hw_attach_bad)) }
            finally { finishEditorWork(editor.draft) }
        }
    }

    private fun attachMany(kind: String, uris: List<Uri>) {
        if (uris.size == 1) { attach(kind, uris.first()); return }
        val editor = mutable.value.editor ?: return
        if (editor.draft.isEmpty() || editor.busy || uris.isEmpty()) return
        mutable.update { it.copy(editor = editor.copy(work = HomeworkEditorWork.Attachment, error = null)) }
        viewModelScope.launch {
            try {
                val result = writes.withLock { withContext(Dispatchers.IO) {
                    importHomeworkAttachmentBatch(container, editor, kind, uris) { saved ->
                        mutable.update { state ->
                            val current = state.editor?.takeIf { it.draft == editor.draft } ?: return@update state
                            state.copy(editor = current.copy(files = current.files + saved))
                        }
                        mutable.value.editor?.let { it.draft == editor.draft && it.files.any { file -> file.id == saved.id } } == true
                    }
                } }
                if (result.failed > 0) editorError(editor.draft, container.app.getString(
                    R.string.ux300_android_attachment_batch_result, result.added, result.failed))
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { editorError(editor.draft, container.app.getString(R.string.hw_attach_bad)) }
            finally { finishEditorWork(editor.draft) }
        }
    }

    private fun toggleDone(id: Long) {
        if (mutable.value.bulkBusy) return
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        val key = PersonalToggleKey(profile, group, id)
        val serial = personalToggleRequests.begin(key) ?: return
        mutable.update { it.copy(personalBusyIds = it.personalBusyIds + id) }
        viewModelScope.launch {
            var terminalOwned = false
            val previous = try {
                writes.withLock { withContext(Dispatchers.IO) {
                    val groupId = container.repo.settings().myGroupId.orEmpty()
                    if (groupId.isEmpty() || groupId != group || epoch != groupEpoch) return@withContext null
                    val hw = container.homework.getById(id) ?: return@withContext null
                    container.homework.markDone(id, !hw.done)
                    HomeworkUndoDone(id, hw.done, groupId, profile)
                } }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "toggleDone", e)
                if (epoch == groupEpoch && currentGroupId == group)
                    container.toasts.show(container.app.getString(R.string.homework_browse_toggle_failed), ToastKind.Bad)
                return@launch
            } finally {
                terminalOwned = personalToggleRequests.finish(key, serial)
                if (terminalOwned) {
                    if (epoch == groupEpoch && currentGroupId == group)
                        mutable.update { it.copy(personalBusyIds = it.personalBusyIds - id) }
                }
            }
            if (previous != null && epoch == groupEpoch && currentGroupId == group &&
                profile == container.profile.databaseName && terminalOwned) {
                val serial = ++undoSerial
                mutable.update { it.copy(undoDone = previous) }
                container.events.emit(AppEvent.PersonalizationChanged)
                viewModelScope.launch {
                    delay(5_000)
                    if (serial == undoSerial) mutable.update { it.copy(undoDone = null) }
                }
            }
        }
    }

    private fun previewPublication(selected: List<Long>) {
        if (container.profile.isGuest || mutable.value.bulkBusy || mutable.value.editor != null) return
        mutable.value.publication?.let {
            mutable.update { state -> state.copy(publication = it.copy(open = true)) }; return
        }
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        val items = mutable.value.groups.flatMap { it.items }.associateBy { it.id }
        val ids = bulkEligibleIds(mutable.value.groups, selected)
        if (ids.isEmpty() || ids.size > 50) return
        mutable.update { it.copy(bulkBusy = true, bulkResult = null) }
        viewModelScope.launch {
            try {
                val context = withContext(Dispatchers.IO) { loadHomeworkShareContext(container, group) }
                    ?: error("No community")
                val author = context.people.firstOrNull { it.self }?.userId ?: error("No membership")
                if (!context.supported) error("Legacy publication")
                val rows = withContext(Dispatchers.IO) { ids.mapNotNull { id ->
                    val current = container.homework.getById(id)?.takeUnless { it.done } ?: return@mapNotNull null
                    PersonalPublicationRow(current, items[id]?.subjectRaw?.ifBlank { current.norm } ?: current.norm,
                        personalPublicationDeadline(current.due))
                } }
                if (sharedScopeCurrent(group, profile) && groupEpoch == epoch) mutable.update {
                    it.copy(publication = PersonalPublicationBatch(group, profile, epoch, context, author, rows))
                }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { if (sharedScopeCurrent(group, profile) && groupEpoch == epoch) mutable.update {
                it.copy(bulkResult = container.app.getString(R.string.ux300_ext_publication_unavailable))
            } }
            finally { if (sharedScopeCurrent(group, profile) && groupEpoch == epoch) mutable.update { it.copy(bulkBusy = false) } }
        }
    }

    private fun publishPersonalBatch() {
        val batch = mutable.value.publication?.takeUnless { it.busy } ?: return
        if (mutable.value.bulkBusy || !publicationAudienceAvailable(batch.audience, batch.context) ||
            !sharedScopeCurrent(batch.groupId, batch.profileName) || batch.epoch != groupEpoch) return
        mutable.update { it.copy(publication = batch.copy(busy = true, error = null), bulkBusy = true) }
        viewModelScope.launch {
            try {
                val client = container.communities ?: error("No communities")
                val token = container.accessToken() ?: error("No session")
                val desk = client.desk(token, batch.context.communityId)
                val home = client.groupHome(token, batch.context.communityId)
                val fresh = HomeworkShareContext(batch.context.communityId, home.name, desk, home.classmates)
                if (home.classmates.firstOrNull { it.self }?.userId != batch.authorId ||
                    !publicationAudienceAvailable(batch.audience, fresh)) error("Audience changed")
                for (row in batch.rows.filterNot { it.sent }) {
                    if (!sharedScopeCurrent(batch.groupId, batch.profileName) || groupEpoch != batch.epoch ||
                        withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } != batch.groupId) break
                    try {
                        if (!row.attempted && !withContext(Dispatchers.IO) { row.matches(container.homework.getById(row.before.id)) })
                            error("Local task changed")
                        mutable.update { state -> state.copy(publication = state.publication?.copy(rows = state.publication.rows.map {
                            if (it.operationId == row.operationId) it.copy(attempted = true, failed = false) else it
                        })) }
                        client.shareHomework(token, batch.context.communityId, row.title, row.before.text.trim(), 0,
                            row.deadline, audience = batch.audience, operationId = row.operationId)
                        if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch) mutable.update { state ->
                            state.copy(publication = state.publication?.copy(rows = state.publication.rows.map {
                                if (it.operationId == row.operationId) it.copy(sent = true, failed = false) else it
                            }))
                        }
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) {
                        if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch) mutable.update { state ->
                            state.copy(publication = state.publication?.copy(rows = state.publication.rows.map {
                                if (it.operationId == row.operationId) it.copy(failed = true) else it
                            }))
                        }
                    }
                }
                if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch) refreshShared(batch.groupId)
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch) mutable.update {
                it.copy(publication = it.publication?.copy(error = container.app.getString(R.string.ux300_ext_publication_unavailable)))
            } }
            finally { if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch) mutable.update {
                it.copy(publication = it.publication?.copy(busy = false), bulkBusy = false)
            } }
        }
    }

    private fun previewPostpone(selected: List<Long>) {
        if (mutable.value.bulkBusy || mutable.value.reschedule?.busy == true || mutable.value.editor != null) return
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        val items = mutable.value.groups.flatMap { it.items }.associateBy { it.id }
        val ids = bulkEligibleIds(mutable.value.groups, selected)
        if (ids.isEmpty() || ids.size > 50) return
        mutable.update { it.copy(bulkBusy = true, bulkResult = null) }
        viewModelScope.launch {
            try {
            val rows = withContext(Dispatchers.IO) { ids.mapNotNull { id ->
                val current = container.homework.getById(id) ?: return@mapNotNull null
                val due = if (current.n < 10) container.homework.computeDueDate(current.norm, current.createdAt, current.n + 1) else null
                HomeworkRescheduleRow(current, items[id]?.subject ?: current.norm, due)
            } }
            if (sharedScopeCurrent(group, profile) && groupEpoch == epoch) mutable.update {
                it.copy(reschedule = HomeworkRescheduleBatch(group, profile, epoch, rows))
            }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { if (sharedScopeCurrent(group, profile) && groupEpoch == epoch) mutable.update {
                it.copy(bulkResult = container.app.getString(R.string.ux300_ext_postpone_failed))
            } }
            finally { if (sharedScopeCurrent(group, profile) && groupEpoch == epoch) mutable.update { it.copy(bulkBusy = false) } }
        }
    }

    private fun applyPostpone(undo: Boolean) {
        val batch = mutable.value.reschedule?.takeUnless { it.busy } ?: return
        if (!sharedScopeCurrent(batch.groupId, batch.profileName) || batch.epoch != groupEpoch || mutable.value.bulkBusy) return
        val targets = batch.rows.filter { if (undo) it.status in setOf("applied", "undoFailed") else it.eligible && it.status in setOf("pending", "failed") }
        if (targets.isEmpty()) return
        mutable.update { it.copy(reschedule = batch.copy(busy = true), bulkBusy = true) }
        viewModelScope.launch {
            try {
                for (row in targets) {
                    val success = try { writes.withLock { withContext(Dispatchers.IO) {
                        container.db.runInTransaction(java.util.concurrent.Callable {
                        applyVerifiedReschedule(row, undo,
                            read = { container.homework.getById(row.before.id) },
                            compute = { n -> container.homework.computeDueDate(row.before.norm, row.before.createdAt, n) },
                            write = { text, n -> container.homework.updateHomework(row.before.id, text, n) },
                            scopeCurrent = { sharedScopeCurrent(batch.groupId, batch.profileName) &&
                                groupEpoch == batch.epoch && container.repo.settings().myGroupId.orEmpty() == batch.groupId })
                        })
                    } } } catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { false }
                    if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch) mutable.update { state ->
                        state.copy(reschedule = state.reschedule?.copy(rows = state.reschedule.rows.map {
                            if (it.before.id != row.before.id) it else it.copy(status = if (success) {
                                if (undo) "restored" else "applied"
                            } else if (undo) "undoFailed" else "failed") }))
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            } finally {
                if (sharedScopeCurrent(batch.groupId, batch.profileName) && groupEpoch == batch.epoch)
                    mutable.update { it.copy(reschedule = it.reschedule?.copy(busy = false), bulkBusy = false) }
            }
        }
    }

    private fun completeMany(selected: List<Long>) {
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        val ids = bulkEligibleIds(mutable.value.groups, selected)
            .filterNot { it in mutable.value.personalBusyIds }
        if (ids.size > 50) {
            mutable.update { it.copy(bulkResult = container.app.getString(R.string.ux300_android_bulk_limit)) }
            return
        }
        if (ids.isEmpty() || mutable.value.bulkBusy) return
        mutable.update { it.copy(bulkBusy = true, bulkResult = null, bulkUndo = emptyList(),
            personalBusyIds = it.personalBusyIds + ids) }
        viewModelScope.launch {
            val completed = ArrayList<HomeworkUndoDone>()
            var failed = 0
            try {
                for (id in ids) {
                    try {
                        val applied = writes.withLock { withContext(Dispatchers.IO) {
                            if (container.repo.settings().myGroupId.orEmpty() != group ||
                                container.profile.databaseName != profile || groupEpoch != epoch) return@withContext false
                            val item = container.homework.getById(id) ?: return@withContext false
                            if (item.done) return@withContext false
                            container.homework.markDone(id, true)
                            true
                        } }
                        if (applied) completed += HomeworkUndoDone(id, false, group, profile)
                        else failed++
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { failed++ }
                }
                if (currentGroupId == group && container.profile.databaseName == profile && groupEpoch == epoch) {
                    mutable.update { it.copy(bulkUndo = completed,
                        bulkResult = container.app.getString(R.string.ux300_android_bulk_result,
                            completed.size, failed)) }
                    container.events.emit(AppEvent.PersonalizationChanged)
                }
            } finally {
                if (currentGroupId == group && container.profile.databaseName == profile && groupEpoch == epoch)
                    mutable.update { it.copy(bulkBusy = false,
                        personalBusyIds = it.personalBusyIds - ids.toSet()) }
            }
        }
    }

    private fun undoMany() {
        val undo = mutable.value.bulkUndo
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        if (undo.isEmpty() || mutable.value.bulkBusy) return
        mutable.update { it.copy(bulkBusy = true, personalBusyIds = it.personalBusyIds + undo.map { row -> row.id }) }
        viewModelScope.launch {
            val failed = ArrayList<HomeworkUndoDone>()
            try {
                for (row in undo) {
                    try {
                        val applied = writes.withLock { withContext(Dispatchers.IO) {
                            if (container.repo.settings().myGroupId.orEmpty() != group ||
                                container.profile.databaseName != profile || groupEpoch != epoch) return@withContext false
                            val current = container.homework.getById(row.id)
                            if (!row.canApply(profile, group, current?.done)) return@withContext false
                            container.homework.markDone(row.id, row.previousDone)
                            true
                        } }
                        if (!applied) failed += row
                    } catch (cancelled: CancellationException) { throw cancelled }
                    catch (_: Exception) { failed += row }
                }
                if (currentGroupId == group && container.profile.databaseName == profile && groupEpoch == epoch) {
                    mutable.update { it.copy(bulkUndo = failed, bulkResult =
                        container.app.getString(R.string.ux300_android_bulk_undo_result,
                            undo.size - failed.size, failed.size)) }
                    container.events.emit(AppEvent.PersonalizationChanged)
                }
            } finally {
                if (currentGroupId == group && container.profile.databaseName == profile && groupEpoch == epoch)
                    mutable.update { it.copy(bulkBusy = false,
                        personalBusyIds = it.personalBusyIds - undo.map { row -> row.id }.toSet()) }
            }
        }
    }

    private fun undoDone() {
        val undo = mutable.value.undoDone ?: return
        if (mutable.value.undoDoneBusy) return
        val serial = undoSerial
        mutable.update { it.copy(undoDoneBusy = true) }
        viewModelScope.launch {
            val restored = try {
                writes.withLock { withContext(Dispatchers.IO) {
                    val groupId = container.repo.settings().myGroupId.orEmpty()
                    if (undo.groupId != currentGroupId) return@withContext false
                    val hw = container.homework.getById(undo.id) ?: return@withContext false
                    if (!undo.canApply(container.profile.databaseName, groupId, hw.done)) return@withContext false
                    container.homework.markDone(undo.id, undo.previousDone)
                    true
                } }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaHomework", "undoDone", e)
                if (serial == undoSerial && mutable.value.undoDone == undo)
                    container.toasts.show(container.app.getString(R.string.homework_browse_undo_failed), ToastKind.Bad)
                return@launch
            } finally {
                if (serial == undoSerial) mutable.update { it.copy(undoDoneBusy = false) }
            }
            if (serial == undoSerial && mutable.value.undoDone == undo) {
                ++undoSerial
                mutable.update { it.copy(undoDone = null, undoDoneBusy = false) }
            }
            if (restored) container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    private fun retryShare() {
        val editor = mutable.value.editor ?: return
        val request = editor.shareRequest?.takeIf { it.operationId != null } ?: return
        if (editor.busy) return
        mutable.update { s -> if (s.editor?.draft == editor.draft) s.copy(editor = s.editor.copy(work = HomeworkEditorWork.Saving, error = null)) else s }
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) { retryHomeworkPublication(container, request) }
                mutable.update { s -> if (s.editor?.draft == editor.draft) s.copy(editor = null) else s }
                container.toasts.show(container.app.getString(R.string.homework_share_selected_success), ToastKind.Ok)
            } catch (cancel: CancellationException) { throw cancel }
            catch (_: Exception) { editorError(editor.draft, container.app.getString(R.string.homework_share_retry_failed)) }
            finally { finishEditorWork(editor.draft) }
        }
    }

    private fun loadShareOptions() {
        val editor = mutable.value.editor?.takeIf { it.share && !it.isEdit } ?: return
        if (editor.shareContext != null || editor.shareLoading) return
        val group = currentGroupId ?: return
        val profile = container.profile.databaseName
        val epoch = groupEpoch
        if (editor.scheduleGroupId != group) return
        mutable.update { s -> if (s.editor?.draft == editor.draft) s.copy(editor = s.editor.copy(shareLoading = true, error = null)) else s }
        viewModelScope.launch {
            val result = try { withContext(Dispatchers.IO) { loadHomeworkShareContext(container, editor.scheduleGroupId) } }
            catch (e: CancellationException) { throw e }
            catch (_: Exception) { null }
            mutable.update { s ->
                val current = s.editor
                if (current?.draft != editor.draft || !current.share) s
                else if (group == currentGroupId && epoch == groupEpoch && profile == container.profile.databaseName)
                    s.copy(editor = current.copy(shareContext = result, shareLoading = false))
                else s.copy(editor = current.copy(shareContext = null, shareLoading = false,
                    sourceChanged = true))
            }
        }
    }

    private fun finishEditorWork(draft: String) {
        mutable.update { current -> if (current.editor?.draft == draft) current.copy(editor = current.editor.copy(work = HomeworkEditorWork.Idle)) else current }
    }

    private fun editorError(draft: String, message: String) {
        mutable.update { current -> if (current.editor?.draft == draft) current.copy(editor = current.editor.copy(error = message)) else current }
    }

    private fun openFile(homeworkId: Long, fileId: String) {
        val opened = runCatching {
            val located = container.homeworkFiles.savedFile(homeworkId, fileId) ?: return@runCatching false
            val uri = FileProvider.getUriForFile(container.app, container.app.packageName + ".fileprovider", located.first)
            val view = Intent(Intent.ACTION_VIEW).setDataAndType(uri, located.second)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
            container.app.startActivity(view)
            true
        }.getOrDefault(false)
        if (!opened) container.toasts.show(container.app.getString(R.string.ux60_file_open_failed), ToastKind.Bad)
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
