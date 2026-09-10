package ru.bgtu_voenmeh.zapara.ui.schedule

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
import ru.bgtu_voenmeh.zapara.data.IntersectionService
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import ru.bgtu_voenmeh.zapara.ui.friends.FriendPalette
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import java.time.LocalDate
import java.time.LocalDateTime

class ScheduleViewModel(
    private val container: AppContainer,
    private val initialDateArg: String?
) : ViewModel() {
    private val mutable = MutableStateFlow(ScheduleUiState())
    val state: StateFlow<ScheduleUiState> = mutable.asStateFlow()
    private var ctx: SchedCtx? = null
    private var allLessons: List<Lesson> = emptyList()
    private val loadGate = Mutex()

    init {
        viewModelScope.launch { bootstrap() }
        viewModelScope.launch {
            container.events.events.collect { bootstrap() }
        }
    }

    fun onEvent(event: ScheduleEvent) {
        when (event) {
            is ScheduleEvent.Need -> viewModelScope.launch { ensurePage(event.date) }
            is ScheduleEvent.Select -> viewModelScope.launch {
                mutable.update { it.copy(selected = event.date) }
                ensureAround(event.date)
            }
            ScheduleEvent.Retry -> viewModelScope.launch { bootstrap() }
            ScheduleEvent.Today -> viewModelScope.launch {
                val today = mutable.value.today
                mutable.update { it.copy(selected = today) }
                ensureAround(today)
            }
            ScheduleEvent.Refresh -> refresh()
            is ScheduleEvent.LongPress -> mutable.update { it.copy(actionsFor = event.lesson) }
            ScheduleEvent.CloseActions -> mutable.update { it.copy(actionsFor = null) }
            is ScheduleEvent.Rename -> openRename(event.lesson)
            is ScheduleEvent.RenameChanged -> mutable.update { s ->
                s.copy(rename = s.rename?.copy(name = event.name, note = event.note, scope = event.scope))
            }
            ScheduleEvent.RenameSave -> saveRename()
            ScheduleEvent.RenameReset -> resetRename()
            ScheduleEvent.RenameCancel -> mutable.update { it.copy(rename = null) }
            is ScheduleEvent.ToggleDone -> toggleDone(event.id)
            is ScheduleEvent.AddHomework -> openHomework(event.lesson)
            is ScheduleEvent.HomeworkEditorText -> mutable.update { s ->
                s.copy(homeworkEditor = s.homeworkEditor?.withText(event.text))
            }
            ScheduleEvent.HomeworkEditorInc -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.inc()) }
            ScheduleEvent.HomeworkEditorDec -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.dec()) }
            ScheduleEvent.HomeworkEditorSave -> saveHomework()
            ScheduleEvent.HomeworkEditorCancel -> mutable.update { it.copy(homeworkEditor = null) }
            is ScheduleEvent.OpenMap -> { }
        }
    }

    private suspend fun bootstrap() = loadGate.withLock {
        try {
            var ensureError: String? = null
            withContext(Dispatchers.IO) {
                try { container.timetable.ensure() } catch (t: Throwable) {
                    if (t is CancellationException) throw t
                    android.util.Log.w("ZaparaSchedule", "ensureData", t)
                    ensureError = t.message ?: t.javaClass.simpleName
                }
            }
            val now = container.clock()
            val today = now.toLocalDate()
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                val all = if (gid.isEmpty()) emptyList() else container.repo.allForGroup(gid)
                val noCache = container.repo.groups().isEmpty()
                Triple(prefs to gid, all, noCache)
            }
            val (prefs, gid) = snap.first
            val all = snap.second
            val noCache = snap.third
            if (gid.isEmpty() && noCache && ensureError != null) {
                mutable.update { it.copy(loaded = true, hasGroup = false, today = today, selected = today, pages = emptyMap(), error = ensureError) }
                return@withLock
            }
            ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            allLessons = all
            val parsedArg = initialDateArg?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
            val selected = parsedArg ?: SmartStart.initialDate(now, Schedule.lessonsForDate(all, gid, today, prefs.periodStart, prefs.weekCount, prefs.parityInvert))
            mutable.update { it.copy(loaded = true, hasGroup = gid.isNotEmpty(), today = today, selected = selected, pages = emptyMap(), error = null) }
            if (ensureError != null && gid.isNotEmpty()) {
                container.toasts.show(container.app.getString(R.string.refresh_fail, ensureError), ToastKind.Bad)
            }
            if (gid.isNotEmpty()) ensureAround(selected)
        } catch (e: CancellationException) { throw e }
        catch (t: Throwable) {
            android.util.Log.e("ZaparaSchedule", "bootstrap", t)
            mutable.update { it.copy(loaded = true, error = t.message ?: t.javaClass.simpleName) }
        }
    }

    private suspend fun ensureAround(date: LocalDate) {
        ensurePage(date)
        ensurePage(date.minusDays(1))
        ensurePage(date.plusDays(1))
    }

    private suspend fun ensurePage(date: LocalDate) {
        if (mutable.value.pages[date] != null) return
        val c = ctx ?: return
        val now = container.clock()
        val page = withContext(Dispatchers.IO) { compose(date, c, now) }
        mutable.update { it.copy(pages = it.pages + (date to page)) }
    }

    private fun compose(date: LocalDate, c: SchedCtx, now: LocalDateTime): DayPage {
        val prefs = container.repo.settings()
        val friends = container.db.friendDao().getAll().map { Friend(it.groupName, it.colorHex, it.enabled, it.memberNames) }
        val groups = container.repo.groups().associate { it.name to it.id }
        return ScheduleComposer.page(
            date, allLessons, c, now,
            displayName = { norm, dow -> container.overrides.displayNameByNorm(norm, dow) },
            homeworkFor = { norm -> container.homework.forSubjectByNorm(norm) },
            friendsFor = { lesson ->
                val hits = IntersectionService.intersections(
                    my = lesson, date = date, friends = friends.filter { it.enabled },
                    strictness = prefs.intersectionStrictness,
                    periodStart = c.periodStart, weekCount = c.weekCount, invert = c.invert,
                    lessonsFor = { fid, dow, parity ->
                        container.repo.allForGroup(fid).filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
                    },
                    resolveId = { name -> groups[name] }
                )
                val byGroup = hits.associateBy { it.friendGroupName }
                val source = if (prefs.alwaysShowAllTrafficLights) friends.filter { it.enabled } else friends.filter { it.enabled && byGroup.containsKey(it.groupName) }
                source.map { f ->
                    val hit = byGroup[f.groupName]
                    FriendDotUi(
                        index = FriendPalette.indexOf(f.colorHex),
                        groupName = f.groupName,
                        members = f.memberNames,
                        score = hit?.score ?: -1,
                        hint = if (hit != null) LessonFormat.friendHint(f.memberNames, f.groupName, hit.score, container.copy)
                        else container.copy.get("friend_hint", f.memberNames.ifBlank { f.groupName }, f.groupName, "")
                    )
                }
            },
            copy = container.copy
        )
    }

    private fun refresh() {
        viewModelScope.launch {
            if (!mutable.value.hasGroup) {
                container.toasts.show(container.app.getString(R.string.pick_group_first), ToastKind.Bad)
                return@launch
            }
            mutable.update { it.copy(refreshing = true) }
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
                android.util.Log.w("ZaparaSchedule", "refresh", e)
                container.toasts.show(container.app.getString(R.string.refresh_fail, e.message ?: e.javaClass.simpleName), ToastKind.Bad)
            } finally {
                mutable.update { it.copy(refreshing = false) }
            }
        }
    }

    private fun openRename(lesson: LessonUi) {
        viewModelScope.launch {
            val existing = withContext(Dispatchers.IO) {
                container.overrides.all().any { it.subjectRawNormalized == lesson.subjectNorm }
            }
            mutable.update {
                it.copy(
                    actionsFor = null,
                    rename = RenameUi(
                        lesson = lesson, name = lesson.name, note = "",
                        scope = 0, hasExisting = existing,
                        original = lesson.original ?: lesson.name,
                        dayName = Parity.dayNumberToTitle(lesson.dayOfWeek)
                    )
                )
            }
        }
    }

    private fun saveRename() {
        val ui = mutable.value.rename ?: return
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                container.overrides.addOrUpdate(
                    ui.lesson.subjectRaw,
                    if (ui.scope == 0) "global" else "weekday:${ui.lesson.dayOfWeek}",
                    ui.name.trim(),
                    ui.note.trim().ifBlank { null }
                )
            }
            mutable.update { it.copy(rename = null) }
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    private fun resetRename() {
        val ui = mutable.value.rename ?: return
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                container.overrides.all().filter { it.subjectRawNormalized == ui.lesson.subjectNorm }
                    .forEach { container.overrides.remove(it.id) }
            }
            mutable.update { it.copy(rename = null) }
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    private fun toggleDone(id: Long) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val hw = container.homework.all().firstOrNull { it.id == id } ?: return@withContext
                container.homework.markDone(id, !hw.done)
            }
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    private fun openHomework(lesson: LessonUi) {
        val c = ctx
        mutable.update {
            it.copy(
                actionsFor = null,
                homeworkEditor = HomeworkEditorState(
                    id = null, subjectRaw = lesson.subjectRaw, subjectDisplay = lesson.name,
                    text = "", n = 1, isEdit = false,
                    dueFor = { n ->
                        if (c == null) null
                        else container.homework.dueDateIn(
                            { gid, dow, parity -> allLessons.filter { l -> l.groupId == gid && l.dayOfWeek == dow && (l.parity == parity || l.parity == 0) } },
                            c, lesson.subjectNorm, LocalDate.now(), n
                        )
                    }
                )
            )
        }
    }

    private fun saveHomework() {
        val editor = mutable.value.homeworkEditor ?: return
        if (!editor.canSave) return
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                if (editor.id == null) container.homework.addHomework(editor.subjectRaw, editor.text.trim(), editor.n)
                else container.homework.updateHomework(editor.id, editor.text.trim(), editor.n)
            }
            mutable.update { it.copy(homeworkEditor = null) }
            container.toasts.show(container.app.getString(R.string.hw_saved), ToastKind.Ok)
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    companion object {
        fun factory(container: AppContainer, dateArg: String?) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                ScheduleViewModel(container, dateArg) as T
        }
    }
}
