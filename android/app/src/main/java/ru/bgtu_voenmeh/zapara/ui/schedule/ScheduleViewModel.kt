package ru.bgtu_voenmeh.zapara.ui.schedule

import android.net.Uri
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
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.HomeworkFileException
import ru.bgtu_voenmeh.zapara.data.IntersectionService
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.Subgroups
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import ru.bgtu_voenmeh.zapara.ui.homework.fileMessage
import ru.bgtu_voenmeh.zapara.ui.friends.FriendPalette
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorWork
import ru.bgtu_voenmeh.zapara.ui.homework.saveHomeworkEditor
import ru.bgtu_voenmeh.zapara.ui.homework.loadHomeworkShareContext
import ru.bgtu_voenmeh.zapara.ui.homework.retryHomeworkPublication
import ru.bgtu_voenmeh.zapara.ui.homework.RequestTokens
import java.time.LocalDate
import java.time.LocalDateTime

class ScheduleViewModel(
    private val container: AppContainer,
    private val initialDateArg: String?
) : ViewModel() {
    private val mutable = MutableStateFlow(ScheduleUiState(guest = container.profile.isGuest,
        profileName = container.profile.databaseName))
    val state: StateFlow<ScheduleUiState> = mutable.asStateFlow()
    private var ctx: SchedCtx? = null
    private var allLessons: List<Lesson> = emptyList()
    private val loadGate = Mutex()
    private val writes = Mutex()
    private val sharedCache = ru.bgtu_voenmeh.zapara.data.communities.SharedHomeworkCache()
    private val projection = ScheduleProjectionController(mutable)
    private val sharedHomework get() = projection.shared.rows
    private val sharedCompletion get() = projection.shared.completions
    private data class SharedActionKey(val profile: String, val group: String, val community: String, val id: String)
    private val sharedActions = RequestTokens<SharedActionKey>()
    private var sharedActionEpoch = 0L
    private var completionEpoch = 0L
    private var undoSerial = 0L
    private fun expireUndo() {
        val serial = ++undoSerial
        viewModelScope.launch { delay(5_000); if (serial == undoSerial) mutable.update { it.copy(undoDone = null, undoShared = null) } }
    }
    private var savingHomework = false
    private var savingRename = false
    private var renameTicket = 0L
    private var subjectRequestTicket = 0L
    private var subgroupUndoSerial = 0L

    init {
        viewModelScope.launch { bootstrap() }
        viewModelScope.launch {
            while (true) {
                delay(ScheduleComposer.millisUntilNextMinute(container.clock()))
                syncClock()
            }
        }
        viewModelScope.launch {
            container.events.events.collect { event ->
                if (ScheduleComposer.resetsPager(event)) bootstrap() else refreshPages()
            }
        }
    }

    fun onEvent(event: ScheduleEvent) {
        when (event) {
            is ScheduleEvent.Need -> viewModelScope.launch { ensurePage(event.date) }
            is ScheduleEvent.Select -> viewModelScope.launch {
                ++renameTicket
                syncClock()
                mutable.update { it.copy(selected = event.date, undoDone = null, undoShared = null) }
                ensureAround(event.date)
            }
            ScheduleEvent.Retry -> viewModelScope.launch { bootstrap() }
            is ScheduleEvent.QuickDay -> viewModelScope.launch {
                ++renameTicket
                syncClock()
                val date=container.clock().toLocalDate().plusDays(event.offset.toLong())
                mutable.update { it.copy(selected=date,undoDone=null,undoShared=null) }
                ensureAround(date)
            }
            ScheduleEvent.Today -> viewModelScope.launch {
                ++renameTicket
                syncClock()
                val today = container.clock().toLocalDate()
                mutable.update { it.copy(today = today, selected = today) }
                ensureAround(today)
            }
            ScheduleEvent.UndoShared -> mutable.value.undoShared?.let { (id, previous) -> toggleShared(id, previous, undo = true) }
            ScheduleEvent.UndoDone -> undoPersonalCompletion()
            ScheduleEvent.SyncClock -> syncClock()
            ScheduleEvent.RefreshShared -> viewModelScope.launch { ctx?.groupId?.let { loadShared(it) } }
            ScheduleEvent.Refresh -> refresh()
            is ScheduleEvent.LongPress -> { ++renameTicket; mutable.update { it.copy(actionsFor = event.lesson) } }
            ScheduleEvent.CloseActions -> { ++renameTicket; mutable.update { it.copy(actionsFor = null) } }
            is ScheduleEvent.Rename -> openRename(event.lesson)
            is ScheduleEvent.RenameChanged -> mutable.update { s ->
                val ui = s.rename
                s.copy(rename = if (ui != null && !ui.busy) ui.copy(name = event.name, note = event.note,
                    scope = event.scope, hasExisting = event.scope in ui.existingScopes, error = null) else ui)
            }
            is ScheduleEvent.RenameSave -> saveRename(event.draft)
            is ScheduleEvent.RenameReset -> resetRename(event.draft)
            ScheduleEvent.RenameCancel -> if (mutable.value.rename?.busy != true) {
                ++renameTicket
                mutable.update { it.copy(rename = null) }
            }
            is ScheduleEvent.SubjectHomework -> openSubjectHomework(event.lesson)
            ScheduleEvent.CloseSubjectHomework -> {
                ++subjectRequestTicket
                mutable.update { it.copy(subjectHomework = null, sharedDetail = null) }
            }
            is ScheduleEvent.OpenHomework -> openExistingHomework(event.row)
            is ScheduleEvent.ToggleShared -> toggleShared(event.id, event.done,
                groupId = event.groupId, profileName = event.profileName)
            is ScheduleEvent.ToggleDone -> toggleDone(event)
            is ScheduleEvent.AddHomework -> openHomework(event.lesson)
            is ScheduleEvent.HomeworkEditorText -> mutable.update { s ->
                s.copy(homeworkEditor = s.homeworkEditor?.withText(event.text))
            }
            is ScheduleEvent.HomeworkEditorShare -> {
                mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.withShare(event.on)) }
                if (event.on) loadHomeworkShareOptions()
            }
            is ScheduleEvent.HomeworkEditorAudience -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.withAudience(event.audience)) }
            ScheduleEvent.HomeworkRetryShareOptions -> loadHomeworkShareOptions()
            ScheduleEvent.HomeworkRetryShare -> retryHomeworkShare()
            ScheduleEvent.HomeworkEditorInc -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.inc()) }
            ScheduleEvent.HomeworkEditorDec -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.dec()) }
            ScheduleEvent.RecalculateHomework -> viewModelScope.launch {
                val editor=mutable.value.homeworkEditor ?: return@launch
                if (editor.busy) return@launch
                mutable.update { it.copy(homeworkEditor = editor.copy(work = HomeworkEditorWork.Recalculating, error = null)) }
                try {
                val prepared=withContext(Dispatchers.IO) {
                    val prefs=container.repo.settings(); val gid=prefs.myGroupId.orEmpty()
                    val context=SchedCtx(gid,prefs.periodStart,prefs.weekCount,prefs.parityInvert)
                    val lessons=container.ownLessons(); val existing=(editor.persistedId ?: editor.id)?.let(container.homework::getById)
                    val anchor=existing?.createdAt ?: editor.creationAnchor(container.clock().toLocalDate())
                    val norm=ru.bgtu_voenmeh.zapara.data.Parity.normalizeSubject(editor.subjectRaw)
                    val due:(Int,String)->LocalDate? = { n,_ -> container.homework.dueDateIn({ g,dow,parity -> lessons.filter { it.groupId==g && it.dayOfWeek==dow && (it.parity==0 || it.parity==parity) } },context,norm,anchor,n) }
                    due to gid
                }
                mutable.update { state -> if(state.homeworkEditor?.draft==editor.draft) state.copy(homeworkEditor=state.homeworkEditor.copy(dueFor=prepared.first,scheduleGroupId=prepared.second,sourceChanged=false)) else state }
                } catch (e: CancellationException) { throw e }
                catch (_: Exception) { homeworkEditorError(editor.draft, container.app.getString(R.string.review_homework_source_changed)) }
                finally { finishHomeworkEditorWork(editor.draft) }
            }
            ScheduleEvent.HomeworkEditorSave -> saveHomework()
            ScheduleEvent.HomeworkApproveDuplicate -> {
                mutable.update { it.copy(homeworkEditor = it.homeworkEditor?.copy(
                    duplicateApproved = true, duplicateWarning = false)) }
                saveHomework()
            }
            ScheduleEvent.HomeworkCancelDuplicate -> mutable.update { it.copy(homeworkEditor =
                it.homeworkEditor?.copy(duplicateWarning = false)) }
            ScheduleEvent.HomeworkEditorCancel -> cancelHomework()
            is ScheduleEvent.HomeworkAttach -> attachHomework(event.kind, event.uri)
            is ScheduleEvent.HomeworkAttachMany -> attachHomeworkMany(event.kind, event.uris)
            is ScheduleEvent.HomeworkRemoveFile -> removeHomeworkFile(event.id)
            is ScheduleEvent.OpenMap -> { }
            is ScheduleEvent.PickSubgroup -> pickSubgroup(event.streamId, event.optionId,
                event.groupId, event.profileName)
            ScheduleEvent.UndoSubgroup -> undoSubgroup()
        }
    }

    private fun openSubjectHomework(lesson: LessonUi) {
        val groupId = ctx?.groupId.orEmpty()
        val request = SubjectHomeworkRequest(++subjectRequestTicket, container.profile.databaseName, groupId, lesson.subjectNorm)
        mutable.update { it.copy(actionsFor = null, subjectHomework = lesson, subjectRows = emptyList(), subjectRowsStatus = SubjectRowsStatus.Loading) }
        viewModelScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != groupId) return@withContext null
                    container.homework.forSubjectByNorm(lesson.subjectNorm).map {
                        HomeworkRowUi(it.id, it.text, it.due?.toString() ?: container.app.getString(R.string.space_day_29), it.status, it.done)
                    }
                } ?: return@launch
                if (!request.matches(subjectRequestTicket, container.profile.databaseName, ctx?.groupId.orEmpty(), mutable.value.subjectHomework)) return@launch
                val shared = sharedHomework.filter { hw ->
                    hw.title.equals(lesson.subjectRaw, true) || hw.title.equals(lesson.name, true)
                }.map { hw -> sharedRow(hw) }
                mutable.update { state ->
                    if (request.matches(subjectRequestTicket, container.profile.databaseName, ctx?.groupId.orEmpty(), state.subjectHomework))
                        state.copy(subjectRows = reconcileSubjectSheetRows(result, shared), subjectRowsStatus = SubjectRowsStatus.Ready)
                    else state
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "subjectHomework", e)
                mutable.update { state ->
                    if (request.matches(subjectRequestTicket, container.profile.databaseName, ctx?.groupId.orEmpty(), state.subjectHomework))
                        state.copy(subjectRowsStatus = SubjectRowsStatus.Failed)
                    else state
                }
            }
        }
    }

    private suspend fun bootstrap() = loadGate.withLock {
        try {
            var ensureError: String? = null
            withContext(Dispatchers.IO) {
                try { container.timetable.ensure() } catch (t: Throwable) {
                    if (t is CancellationException) throw t
                    android.util.Log.w("ZaparaSchedule", "ensureData", t)
                    ensureError = when (t) {
                        is java.net.UnknownHostException, is java.net.SocketTimeoutException,
                        is java.net.ConnectException -> container.app.getString(R.string.load_fail_network)
                        else -> t.message?.takeIf { it.any { ch -> ch in '\u0400'..'\u04FF' } }
                            ?: container.app.getString(R.string.load_fail_network)
                    }
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
            if (ctx?.groupId != gid) {
                ++sharedActionEpoch; sharedActions.clear()
                ++renameTicket
                ++completionEpoch
                ++subjectRequestTicket
                ++subgroupUndoSerial
                mutable.update { it.copy(subjectHomework = null, subjectRows = emptyList(), undoSubgroup = null,
                    undoDone = null, completionBusyIds = emptySet(), undoDoneBusy = false,
                    sharedBusyIds = emptySet()) }
                sharedCache.clear(); projection.purge()
            }
            ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            allLessons = all
            val parsedArg = initialDateArg?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
            val visible = ru.bgtu_voenmeh.zapara.data.Subgroups.visible(all, container.subgroupChoices(gid))
            val selected = if (mutable.value.loaded) mutable.value.selected else parsedArg ?: today
            mutable.update { it.copy(loaded = true, hasGroup = gid.isNotEmpty(), groupId = gid,
                today = today, selected = selected, pages = emptyMap(), error = null, now = now,
                sourceStatus = ru.bgtu_voenmeh.zapara.ui.settings.SettingsLogic.updatedLine(prefs.lastFetchedAt, now, container.copy, allLessons.isNotEmpty())) }
            if (ensureError != null && gid.isNotEmpty()) {
                container.toasts.show(container.app.getString(R.string.refresh_fail, ensureError), ToastKind.Bad)
            }
            if (gid.isNotEmpty()) ensureAround(selected)
            loadShared(gid)
        } catch (e: CancellationException) { throw e }
        catch (t: Throwable) {
            android.util.Log.e("ZaparaSchedule", "bootstrap", t)
            val message = t.message?.takeIf { it.any { ch -> ch in '\u0400'..'\u04FF' } }
                ?: container.app.getString(R.string.load_fail)
            mutable.update { it.copy(loaded = true, error = message) }
        }
    }

    private suspend fun refreshPages() = loadGate.withLock {
        try {
            val now = container.clock()
            val today = now.toLocalDate()
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                val all = if (gid.isEmpty()) emptyList() else container.repo.allForGroup(gid)
                Triple(prefs, gid, all)
            }
            val (prefs, gid, all) = snap
            val changedGroup = ctx?.groupId != gid
            if (changedGroup) {
                ++sharedActionEpoch; sharedActions.clear()
                ++renameTicket
                ++completionEpoch
                ++subjectRequestTicket
                ++subgroupUndoSerial
                mutable.update { it.copy(pages = emptyMap(), actionsFor = null, rename = null,
                    subjectHomework = null, subjectRows = emptyList(), undoSubgroup = null,
                    undoDone = null, undoShared = null, completionBusyIds = emptySet(), undoDoneBusy = false,
                    sharedBusyIds = emptySet()) }
                sharedCache.clear(); projection.purge()
            }
            ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            allLessons = all
            val rolled = ScheduleComposer.syncToday(today, mutable.value.today, mutable.value.selected)
            val dates = (mutable.value.pages.keys + rolled.second).distinct()
            mutable.update { it.copy(hasGroup = gid.isNotEmpty(), groupId = gid,
                today = rolled.first, selected = rolled.second, error = null) }
            dates.forEach { ensurePage(it, force = true) }
            loadShared(gid)
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSchedule", "refreshPages", e)
        }
    }

    private fun syncClock() {
        val clockToday = container.clock().toLocalDate()
        val cur = mutable.value
        val (today, selected) = ScheduleComposer.syncToday(clockToday, cur.today, cur.selected)
        mutable.update { it.copy(today = today, selected = selected, now = container.clock()) }
        if (selected != cur.selected) viewModelScope.launch { ensureAround(selected) }
    }

    private suspend fun ensureAround(date: LocalDate) {
        ensurePage(date)
        for (offset in 1L..3L) ensurePage(date.minusDays(offset))
        for (offset in 1L..6L) ensurePage(date.plusDays(offset))
    }

    private suspend fun ensurePage(date: LocalDate, force: Boolean = false) {
        val c=ctx ?: return
        val now=container.clock()
        val lessons=allLessons
        val choices=container.subgroupChoices(c.groupId)
        projection.ensure(date,force,
            contextCurrent={ ctx==c && allLessons==lessons && choices==container.subgroupChoices(c.groupId) },
            compose={ shared -> withContext(Dispatchers.IO) { compose(date,c,now,lessons,choices,shared) } })
    }

    private fun compose(date: LocalDate, c: SchedCtx, now: LocalDateTime, dayLessons: List<Lesson>, choices: Map<String, String>, shared: ScheduleProjectionController.Shared): DayPage {
        val prefs = container.repo.settings()
        val friends = container.db.friendDao().getAll().map { Friend(it.groupName, it.colorHex, it.enabled, it.memberNames) }
        val groups = container.repo.groups()
        val apiCache = ru.bgtu_voenmeh.zapara.data.api.TimetableApiCache(container.repo.store)
        val friendRows = friends.filter { it.enabled }.take(5).mapNotNull { friend ->
            val id = groups.firstOrNull { it.id == friend.groupName || it.name.equals(friend.groupName, ignoreCase = true) }?.id
                ?: return@mapNotNull null
            if (id == c.groupId || !apiCache.canIntersect(c.groupId, id)) return@mapNotNull null
            val lessons = container.repo.allForGroup(id)
            if (apiCache.read(id) == null && lessons.isEmpty()) return@mapNotNull null
            Triple(friend, id, lessons)
        }
        val enabled = friendRows.map { it.first }
        val ids = friendRows.associate { it.first.groupName to it.second }
        val lessonsById = friendRows.associate { it.second to it.third }
        val page = ScheduleComposer.page(
            date, dayLessons, c, now,
            displayName = { norm, dow -> container.overrides.displayNameByNorm(norm, dow) },
            homeworkFor = { norm -> container.homework.forSubjectByNorm(norm) },
            friendsFor = { lesson ->
                val threshold = ru.bgtu_voenmeh.zapara.ui.friends.Strictness.nearest(prefs.intersectionStrictness)
                val hits = IntersectionService.intersections(
                    my = lesson, date = date, friends = enabled,
                    strictness = 0,
                    periodStart = c.periodStart, weekCount = c.weekCount, invert = c.invert,
                    lessonsFor = { fid, dow, parity -> lessonsById[fid].orEmpty().filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) } },
                    resolveId = { name -> ids[name] }
                )
                val byGroup = hits.associateBy { it.friendGroupName }
                val source = if (prefs.alwaysShowAllTrafficLights) enabled else enabled.filter { (byGroup[it.groupName]?.score ?: 0) >= threshold }
                source.map { f ->
                    val hit = byGroup[f.groupName]
                    val visibleScore = hit?.score?.takeIf { it >= threshold } ?: -1
                    val baseHint = LessonFormat.friendHint(f.memberNames, f.groupName, hit?.score ?: -1, container.copy)
                    FriendDotUi(
                        index = FriendPalette.indexOf(f.colorHex),
                        groupName = f.groupName,
                        members = f.memberNames,
                        score = visibleScore,
                        hint = if (hit != null && visibleScore < 0) "$baseHint · ${container.copy.get("friend_below_level")}" else baseHint,
                        intersectionScore = hit?.score ?: -1
                    )
                }
            },
            copy = container.copy,
            choices = choices
        )
        val localDeadlines = ScheduleComposer.deadlines(date, page.lessons.map { it.subjectNorm }.toSet(), container.homework.all())
            .map { HomeworkRowUi(it.id, it.text, container.app.getString(R.string.space_day_31, (it.norm).toString(), (it.due?.let { date -> date.toString() } ?: container.app.getString(R.string.space_day_30)).toString()), it.status, it.done) }
        val source = when {
            apiCache.read(c.groupId) == null && dayLessons.isEmpty() -> container.app.getString(R.string.space_day_32)
            date < (apiCache.read(c.groupId)?.period?.start ?: c.periodStart) -> container.app.getString(R.string.space_day_33)
            else -> null
        }
        val subjects = page.lessons.flatMap { listOf(it.subjectRaw, it.name, it.subjectNorm) }.toSet()
        val sharedDeadlines = shared.rows.filter { hw ->
            hw.deadlineAt?.atZone(java.time.ZoneId.systemDefault())?.toLocalDate()?.let { due -> due >= date && due <= date.plusDays(2) }
                ?: subjects.any { it.equals(hw.title, true) }
        }.map { sharedRow(it, shared.completions) }
        val transfers = if (source != null) emptyList() else runCatching {
            val graph = runCatching { container.mapStore.campusGraph() }.getOrDefault(ru.bgtu_voenmeh.zapara.data.campus.CampusGraph.empty)
            val ambiguous = ScheduleComposer.conflictPairs(page.lessons)
                .flatMap { (first, second) -> listOf(page.lessons[first], page.lessons[second]) }.toSet()
            page.lessons.sortedBy { it.timeStart }.zipWithNext().map { (from, to) ->
                val start = ru.bgtu_voenmeh.zapara.data.campus.CampusRouter.resolveClassroom(graph, from.classroomRaw)
                val end = ru.bgtu_voenmeh.zapara.data.campus.CampusRouter.resolveClassroom(graph, to.classroomRaw)
                val seconds = if (from in ambiguous || to in ambiguous || from.remote || to.remote || start == null || end == null) null else
                    ru.bgtu_voenmeh.zapara.data.campus.CampusRouter.find(graph, start.id, end.id).route?.seconds
                LessonTransfer(from.room, to.room, to.classroomRaw,
                    ru.bgtu_voenmeh.zapara.ui.StudyPlanning.assessTransfer(from.timeEnd, to.timeStart, seconds))
            }
        }.getOrDefault(emptyList())
        return page.copy(lessons = if (source != null) emptyList() else page.lessons, deadlines = localDeadlines + sharedDeadlines,
            dataState = source, transfers = transfers)
    }

    private fun pickSubgroup(streamId: String, optionId: String,
        expectedGroup: String?, expectedProfile: String?) {
        val gid = ctx?.groupId?.takeIf { it.isNotEmpty() } ?: return
        val profile = container.profile.databaseName
        if (expectedGroup != null && expectedGroup != gid) return
        if (expectedProfile != null && expectedProfile != profile) return
        viewModelScope.launch {
            try {
                val change = writes.withLock { withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != gid ||
                        (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container?.profile
                            ?.let { it != container.profile } == true) return@withContext null
                    val streams = Subgroups.index(container.repo.allForGroup(gid)).streams
                    if (!subgroupOptionExists(streams, streamId, optionId)) return@withContext null
                    val (before, after) = container.subgroups.selectWithPrevious(profile, gid, streamId, optionId)
                    SubgroupUndoUi(profile, gid, streamId, before, after)
                } }
                if (change != null && ctx?.groupId == gid) {
                    val serial = ++subgroupUndoSerial
                    mutable.update { it.copy(undoSubgroup = change) }
                    container.events.emit(AppEvent.SubgroupChanged)
                    viewModelScope.launch { delay(5_000); if (serial == subgroupUndoSerial)
                        mutable.update { it.copy(undoSubgroup = null) } }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "subgroup", e)
                container.toasts.show(container.app.getString(R.string.uxnext_subgroup_failed), ToastKind.Bad)
            }
        }
    }

    private fun undoSubgroup() {
        val undo = mutable.value.undoSubgroup ?: return
        ++subgroupUndoSerial
        mutable.update { it.copy(undoSubgroup = null) }
        viewModelScope.launch {
            try {
                val restored = writes.withLock { withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != undo.groupId ||
                        (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container?.profile
                            ?.let { it != container.profile } == true) return@withContext false
                    val streams = Subgroups.index(container.repo.allForGroup(undo.groupId)).streams
                    val current = container.subgroups.read(undo.profile, undo.groupId)[undo.streamId]
                    undo.allows(container.profile.databaseName, undo.groupId, current, streams) &&
                        container.subgroups.restoreIfCurrent(undo.profile, undo.groupId, undo.streamId, undo.after, undo.before)
                } }
                if (restored) container.events.emit(AppEvent.SubgroupChanged)
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "subgroup undo", e)
                container.toasts.show(container.app.getString(R.string.uxnext_subgroup_failed), ToastKind.Bad)
            }
        }
    }

    private fun refresh() {
        if (mutable.value.refreshing) return
        if (!mutable.value.hasGroup) {
            container.toasts.show(container.app.getString(R.string.pick_group_first), ToastKind.Bad)
            return
        }
        mutable.update { it.copy(refreshing = true) }
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) {
                    if (!container.timetable.pull()) {
                        throw IllegalStateException(container.api.lastError ?: container.app.getString(R.string.refresh_fail, ""))
                    }
                }
                val copiedAt=withContext(Dispatchers.IO) { container.repo.settings().lastFetchedAt }
                mutable.update { it.copy(sourceStatus = ru.bgtu_voenmeh.zapara.ui.settings.SettingsLogic.updatedLine(copiedAt, container.clock(), container.copy, allLessons.isNotEmpty())) }
                container.events.emit(AppEvent.ScheduleChanged)
                container.toasts.show(container.app.getString(R.string.refresh_ok), ToastKind.Ok)
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "refresh", e)
                val copiedAt=withContext(Dispatchers.IO) { container.repo.settings().lastFetchedAt }
                mutable.update { it.copy(sourceStatus = container.app.getString(if (e is java.net.UnknownHostException || e is java.net.ConnectException || e is java.net.SocketTimeoutException) R.string.space_day_no_network_copy else R.string.space_day_copy_error, ru.bgtu_voenmeh.zapara.ui.settings.SettingsLogic.updatedLine(copiedAt, container.clock(), container.copy, allLessons.isNotEmpty()))) }
                container.toasts.show(container.app.getString(R.string.refresh_fail, e.message ?: e.javaClass.simpleName), ToastKind.Bad)
            } finally {
                mutable.update { it.copy(refreshing = false) }
            }
        }
    }

    private fun openRename(lesson: LessonUi) {
        if (savingRename) return
        val ticket = ++renameTicket
        val group = ctx?.groupId ?: return
        val profile = container.profile.databaseName
        val selectedDate = mutable.value.selected
        mutable.update { it.copy(actionsFor = null) }
        viewModelScope.launch {
            try {
                val existing = withContext(Dispatchers.IO) {
                    val rows = container.overrides.all().filter { it.subjectRawNormalized == lesson.subjectNorm }
                    val scopes = rows.mapNotNull { row -> when (row.scope) {
                        "global" -> 0
                        "weekday:${lesson.dayOfWeek}" -> 1
                        else -> null
                    } }.toSet()
                    val matching = container.repo.allForGroup(group)
                        .filter { Parity.sameSubject(it.subjectNormalized, lesson.subjectNorm) }
                        .sortedWith(compareBy({ it.dayOfWeek }, { it.timeStart }, { it.parity }))
                    fun label(row: ru.bgtu_voenmeh.zapara.data.Lesson) =
                        "${Parity.dayNumberToTitle(row.dayOfWeek)} · ${row.timeStart}–${row.timeEnd}"
                    Triple(scopes, container.overrides.noteByNorm(lesson.subjectNorm, lesson.dayOfWeek),
                        matching.map(::label) to matching.filter { it.dayOfWeek == lesson.dayOfWeek }.map(::label))
                }
                if (ticket != renameTicket || ctx?.groupId != group || mutable.value.selected != selectedDate ||
                    container.profile.databaseName != profile) return@launch
                mutable.update { state -> state.copy(rename = RenameUi(
                    lesson = lesson, name = lesson.name, note = existing.second,
                    scope = 0, hasExisting = 0 in existing.first, existingScopes = existing.first,
                    original = lesson.original ?: lesson.name,
                    dayName = Parity.dayNumberToTitle(lesson.dayOfWeek),
                    profileName = profile, groupId = group, selectedDate = selectedDate,
                    affectedGlobal = existing.third.first, affectedWeekday = existing.third.second
                )) }
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) {
                if (ticket == renameTicket) container.toasts.show(container.app.getString(R.string.ux60_rename_load_failed), ToastKind.Bad)
            }
        }
    }

    private fun renameCurrent(ticket: Long, ui: RenameUi): Boolean = ticket == renameTicket &&
        container.profile.databaseName == ui.profileName && ctx?.groupId == ui.groupId &&
        mutable.value.selected == ui.selectedDate && mutable.value.rename?.lesson == ui.lesson

    private fun saveRename(target: RenameUi) {
        if (savingRename) return
        val ui = mutable.value.rename?.takeIf { !it.busy && it == target } ?: return
        val ticket = ++renameTicket
        savingRename = true
        mutable.update { it.copy(rename = ui.copy(busy = true, error = null)) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        if (container.repo.settings().myGroupId.orEmpty() != ui.groupId ||
                            container.profile.databaseName != ui.profileName) error("rename scope changed")
                        container.overrides.addOrUpdate(
                            ui.lesson.subjectRaw,
                            renameScopeKey(ui.scope, ui.lesson.dayOfWeek),
                            ui.name.trim(),
                            ui.note.trim().ifBlank { null }
                        )
                    }
                }
                if (renameCurrent(ticket, ui)) mutable.update { it.copy(rename = null) }
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                if (renameCurrent(ticket, ui)) mutable.update { it.copy(rename = ui.copy(error = container.app.getString(R.string.ux60_rename_save_failed))) }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "rename", e)
                if (renameCurrent(ticket, ui)) mutable.update { it.copy(rename = ui.copy(error = container.app.getString(R.string.ux60_rename_save_failed))) }
            } finally {
                savingRename = false
            }
        }
    }

    private fun resetRename(target: RenameUi) {
        if (savingRename) return
        val ui = mutable.value.rename?.takeIf { !it.busy && it.hasExisting && it == target } ?: return
        val ticket = ++renameTicket
        savingRename = true
        mutable.update { it.copy(rename = ui.copy(busy = true, error = null)) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        if (container.repo.settings().myGroupId.orEmpty() != ui.groupId ||
                            container.profile.databaseName != ui.profileName) error("rename scope changed")
                        val targetScope = renameScopeKey(ui.scope, ui.lesson.dayOfWeek)
                        renameResetIds(container.overrides.all(), ui.lesson.subjectNorm, targetScope)
                            .forEach(container.overrides::remove)
                    }
                }
                if (renameCurrent(ticket, ui)) mutable.update { it.copy(rename = null) }
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                if (renameCurrent(ticket, ui)) mutable.update { it.copy(rename = ui.copy(error = container.app.getString(R.string.ux60_rename_reset_failed))) }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "rename reset", e)
                if (renameCurrent(ticket, ui)) mutable.update { it.copy(rename = ui.copy(error = container.app.getString(R.string.ux60_rename_reset_failed))) }
            } finally {
                savingRename = false
            }
        }
    }

    private fun sharedRow(hw: ru.bgtu_voenmeh.zapara.data.communities.CommunityHomework, completions: Map<String,ru.bgtu_voenmeh.zapara.data.communities.HomeworkCompletion> = projection.shared.completions) = HomeworkRowUi(
        0, hw.body, container.app.getString(R.string.space_day_35, (hw.title).toString(), (hw.deadlineAt?.atZone(java.time.ZoneId.systemDefault())?.toLocalDate() ?: container.app.getString(R.string.space_day_34)).toString()),
        if (hw.deadlineAt?.isBefore(java.time.Instant.now()) == true) "overdue" else "active", completions[hw.homeworkId]?.completed == true, hw.homeworkId,
        hw.canComplete, container.app.getString(if (hw.audience?.selected == true) R.string.homework_audience_selected else R.string.homework_audience_all))

    private fun purgeShared() {
        ++sharedActionEpoch; sharedActions.clear()
        mutable.update { it.copy(sharedBusyIds = emptySet()) }
        sharedCache.clear(); projection.purge()
    }

    private suspend fun loadShared(gid: String) {
        if (container.profile.isGuest) return
        val api = container.communities ?: return
        val token = try { withContext(Dispatchers.IO) { container.accessToken() } }
            catch (e: CancellationException) { throw e } catch (_: Exception) { return }
        val result = sharedCache.refresh(api, token, gid)
        if (!result.applied || ctx?.groupId != gid) return
        if (result.revoked) { purgeShared(); return }
        result.snapshot?.let { snapshot ->
            val assigned=projection.assign(snapshot.communityId,snapshot.rows,snapshot.completions)
            mutable.value.pages.keys.toList().forEach { ensurePage(it, force = true) }
            if(!projection.isCurrent(assigned)) return
            mutable.update { state ->
                val sheet = state.subjectHomework
                val matching = if (sheet == null) emptyList() else snapshot.rows.filter { hw ->
                    hw.title.equals(sheet.subjectRaw, true) || hw.title.equals(sheet.name, true)
                }.map { sharedRow(it) }
                state.copy(subjectRows = if (sheet == null) state.subjectRows else
                    reconcileSubjectSheetRows(state.subjectRows, matching),
                    sharedDetail = state.sharedDetail?.sharedId?.let { id ->
                        snapshot.rows.firstOrNull { hw -> hw.homeworkId == id }?.let { hw -> sharedRow(hw) }
                    })
            }
        }
    }

    private fun completionScope() = ScheduleProjectionController.CompletionScope(
        (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container?.profile ?: container.profile,
        ctx?.groupId, projection.shared.communityId)

    private fun toggleShared(id: String, done: Boolean, undo: Boolean = false,
        groupId: String? = null, profileName: String? = null) {
        val api = container.communities ?: return
        val operation=projection.shared
        if (operation.rows.firstOrNull { it.homeworkId == id }?.canComplete != true) return
        val community = operation.communityId ?: return
        val group = ctx?.groupId ?: return
        val profile = container.profile.databaseName
        if (groupId != null && groupId != group || profileName != null && profileName != profile) return
        val actionKey = SharedActionKey(profile, group, community, id)
        val serial = sharedActions.begin(actionKey) ?: return
        val epoch = sharedActionEpoch
        mutable.update { it.copy(sharedBusyIds = it.sharedBusyIds + id) }
        val operationScope=ScheduleProjectionController.CompletionScope(container.profile,group,community)
        viewModelScope.launch {
            var operationToken: String? = null
            try {
                val token = withContext(Dispatchers.IO) { container.accessToken() } ?: return@launch
                operationToken=token
                if(operationScope!=completionScope() || !projection.isCurrent(operation)) return@launch
                val previous = operation.completions[id]?.completed == true
                val result = api.upsertCompletion(token, community, id, done, operation.completions[id]?.revision ?: 0)
                if (operationScope!=completionScope() || !projection.isCurrent(operation)) return@launch
                val completed=projection.complete(result)
                mutable.update { it.copy(undoShared = if (undo) null else id to previous, undoDone = null) }
                if (!undo) expireUndo()
                mutable.value.pages.keys.toList().forEach { ensurePage(it, force = true) }
                if(!projection.isCurrent(completed)) return@launch
                mutable.update { it.copy(subjectRows = it.subjectRows.map { row -> if (row.sharedId == id) row.copy(done = done) else row }) }
            } catch (e: CancellationException) { throw e }
            catch (e: ru.bgtu_voenmeh.zapara.data.communities.CommunityClientException) {
                val currentScope=completionScope()
                if (e.failure in setOf(ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.Conflict, ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure.RevisionConflict) && operationScope == currentScope && operationToken != null) {
                    try {
                        val latest = api.getCompletion(operationToken, community, id)
                        if (operationScope == completionScope() && projection.isCurrent(operation)) {
                            val corrected = projection.complete(latest)
                            mutable.value.pages.keys.toList().forEach { ensurePage(it, force = true) }
                            if (projection.isCurrent(corrected)) mutable.update { state -> state.copy(subjectRows = state.subjectRows.map { row ->
                                if (row.sharedId == id) row.copy(done = latest.completed) else row
                            }) }
                        }
                    } catch (cancel: CancellationException) { throw cancel }
                    catch (_: Exception) { }
                    container.toasts.show(container.app.getString(R.string.homework_completion_conflict), ToastKind.Bad)
                    return@launch
                }
                val purged=projection.completionDenied(operationScope,currentScope,e.failure,
                    invalidateCache={sharedCache.invalidate(group,community,operationToken)},purgeCurrent=::purgeShared)
                if(purged || operationScope==currentScope) container.toasts.show(container.app.getString(R.string.homework_save_failed), ToastKind.Bad)
            }
            catch (_: Exception) { if(operationScope==completionScope()) container.toasts.show(container.app.getString(R.string.homework_save_failed), ToastKind.Bad) }
            finally {
                if (sharedActions.finish(actionKey, serial)) {
                    if (epoch == sharedActionEpoch && operationScope == completionScope())
                        mutable.update { it.copy(sharedBusyIds = it.sharedBusyIds - id) }
                }
            }
        }
    }

    private fun openExistingHomework(row: HomeworkRowUi) {
        if (mutable.value.homeworkEditor != null) return
        if (row.sharedId != null) {
            val current=projection.shared.rows.firstOrNull { it.homeworkId==row.sharedId } ?: return
            mutable.update { it.copy(subjectHomework=null,sharedDetail=sharedRow(current)) }
            return
        }
        viewModelScope.launch {
            val hw = withContext(Dispatchers.IO) { container.homework.all().firstOrNull { it.id == row.id } } ?: return@launch
            val (files, missingFiles) = withContext(Dispatchers.IO) {
                ru.bgtu_voenmeh.zapara.ui.homework.loadEditorFiles(container, hw.id)
            }
            if (mutable.value.homeworkEditor != null) return@launch
            val lesson = mutable.value.subjectHomework
            val context = ctx
            val snapshot = allLessons
            val choices = context?.let { container.subgroupChoices(it.groupId) }.orEmpty()
            mutable.update { it.copy(subjectHomework = null, homeworkEditor = HomeworkEditorState(hw.id, lesson?.subjectRaw ?: hw.norm, lesson?.name ?: hw.norm, hw.text, hw.n, true,
                { n, _ -> if (context == null) hw.due else container.homework.dueDateIn({ gid, dow, parity -> ru.bgtu_voenmeh.zapara.data.HomeworkDue.lessonsOnChosenDay(snapshot.filter { row -> row.groupId == gid }, choices, dow, parity) }, context, hw.norm, hw.createdAt, n) }, files, java.util.UUID.randomUUID().toString(),
                missingFileIds = missingFiles)) }
        }
    }

    private fun toggleDone(event: ScheduleEvent.ToggleDone) {
        val group = ctx?.groupId ?: return
        val profile = container.profile.databaseName
        if (event.groupId != null && event.groupId != group ||
            event.profileName != null && event.profileName != profile ||
            event.id in mutable.value.completionBusyIds) return
        val epoch = completionEpoch
        mutable.update { it.copy(completionBusyIds = it.completionBusyIds + event.id) }
        viewModelScope.launch {
            try {
                val undo = writes.withLock { withContext(Dispatchers.IO) {
                    if (container.repo.settings().myGroupId.orEmpty() != group) return@withContext null
                    val hw = container.homework.getById(event.id) ?: return@withContext null
                    if (event.expectedDone != null && hw.done != event.expectedDone) return@withContext null
                    container.homework.markDone(event.id, !hw.done)
                    ScheduleCompletionUndo(event.id, hw.done, profile, group)
                } }
                if (undo == null) {
                    if (epoch == completionEpoch) container.toasts.show(container.app.getString(R.string.ux60_task_changed), ToastKind.Bad)
                    return@launch
                }
                if (epoch == completionEpoch && ctx?.groupId == group) {
                    mutable.update { it.copy(undoDone = undo, undoShared = null) }
                    expireUndo()
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) {
                if (epoch == completionEpoch) container.toasts.show(container.app.getString(R.string.ux60_completion_failed), ToastKind.Bad)
            } finally {
                if (epoch == completionEpoch) mutable.update { it.copy(completionBusyIds = it.completionBusyIds - event.id) }
            }
        }
    }

    private fun undoPersonalCompletion() {
        val undo = mutable.value.undoDone ?: return
        if (mutable.value.undoDoneBusy) return
        val epoch = completionEpoch
        mutable.update { it.copy(undoDoneBusy = true) }
        viewModelScope.launch {
            try {
                val restored = writes.withLock { withContext(Dispatchers.IO) {
                    val currentGroup = container.repo.settings().myGroupId.orEmpty()
                    val hw = container.homework.getById(undo.id) ?: return@withContext false
                    if (!undo.canApply(container.profile.databaseName, currentGroup, hw.done)) return@withContext false
                    container.homework.markDone(undo.id, undo.previousDone)
                    true
                } }
                if (epoch == completionEpoch && mutable.value.undoDone == undo) {
                    if (restored) {
                        ++undoSerial
                        mutable.update { it.copy(undoDone = null, undoShared = null) }
                    } else mutable.update { it.copy(undoDone = null) }
                }
                if (restored) container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) {
                if (epoch == completionEpoch && mutable.value.undoDone == undo)
                    container.toasts.show(container.app.getString(R.string.ux60_undo_failed), ToastKind.Bad)
            } finally {
                if (epoch == completionEpoch) mutable.update { it.copy(undoDoneBusy = false) }
            }
        }
    }

    private fun openHomework(lesson: LessonUi) {
        if (mutable.value.homeworkEditor != null) return
        val c = ctx
        val anchor = mutable.value.selected
        val snapshotLessons = allLessons
        val snapshotChoices = c?.let { container.subgroupChoices(it.groupId) }.orEmpty()
        mutable.update {
            it.copy(
                actionsFor = null,
                homeworkEditor = HomeworkEditorState(
                    id = null, subjectRaw = lesson.subjectRaw, subjectDisplay = lesson.name,
                    text = "", n = 1, isEdit = false,
                    draft = java.util.UUID.randomUUID().toString(), anchorDate = anchor, scheduleGroupId = c?.groupId,
                    dueFor = { n, _ ->
                        if (c == null) null
                        else {
                            val choices = snapshotChoices
                            container.homework.dueDateIn(
                                { gid, dow, parity ->
                                    ru.bgtu_voenmeh.zapara.data.HomeworkDue.lessonsOnChosenDay(
                                        snapshotLessons.filter { l -> l.groupId == gid },
                                        if (gid == c.groupId) choices else emptyMap(),
                                        dow,
                                        parity
                                    )
                                },
                                c, lesson.subjectNorm, anchor, n
                            )
                        }
                    }
                )
            )
        }
    }

    private fun saveHomework() {
        if (savingHomework) return
        val editor = mutable.value.homeworkEditor ?: return
        if (!editor.canSave) return
        savingHomework = true
        mutable.update { it.copy(homeworkEditor = editor.copy(work = HomeworkEditorWork.Saving, error = null)) }
        viewModelScope.launch {
            try {
                val outcome = writes.withLock {
                    withContext(Dispatchers.IO) {
                        saveHomeworkEditor(container, editor) { progress ->
                            mutable.update { current -> if (current.homeworkEditor?.draft == editor.draft)
                                current.copy(homeworkEditor = progress.copy(work = HomeworkEditorWork.Saving)) else current }
                        }
                    }
                }
                mutable.update { current -> if (current.homeworkEditor?.draft != editor.draft) current else if (!outcome.sent && current.homeworkEditor.shareRequest != null)
                    current.copy(homeworkEditor = current.homeworkEditor.copy(error = outcome.note)) else current.copy(homeworkEditor = null) }
                val note = outcome.note.ifBlank { container.app.getString(R.string.hw_saved) }
                container.toasts.show(note, if (editor.share && !outcome.sent) ToastKind.Bad else ToastKind.Ok)
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                throw e
            } catch (_: ru.bgtu_voenmeh.zapara.ui.homework.DuplicateHomework) {
                mutable.update { current -> if (current.homeworkEditor?.draft == editor.draft)
                    current.copy(homeworkEditor = current.homeworkEditor.copy(
                        duplicateWarning = true, error = null)) else current }
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "homework", e)
                val changed = e is ru.bgtu_voenmeh.zapara.ui.homework.HomeworkScheduleChanged
                val message = container.app.getString(if (changed) R.string.review_homework_source_changed
                    else if (mutable.value.homeworkEditor?.persistedId != null) R.string.homework_ux_partial_save_failed else R.string.homework_ux_save_failed)
                mutable.update { cur -> if (cur.homeworkEditor?.draft == editor.draft) cur.copy(homeworkEditor = cur.homeworkEditor.copy(error = message,
                    sourceChanged = cur.homeworkEditor.sourceChanged || changed)) else cur }
            } finally {
                savingHomework = false
                finishHomeworkEditorWork(editor.draft)
            }
        }
    }

    private fun cancelHomework() {
        val editor = mutable.value.homeworkEditor
        if (editor?.busy == true) return
        mutable.update { it.copy(homeworkEditor = null) }
        if (editor != null && editor.draft.isNotEmpty()) {
            viewModelScope.launch(Dispatchers.IO) { container.homeworkFiles.discard(editor.draft) }
        }
    }

    private fun attachHomework(kind: String, uri: Uri) {
        val editor = mutable.value.homeworkEditor ?: return
        if (editor.draft.isEmpty() || editor.busy) return
        mutable.update { it.copy(homeworkEditor = editor.copy(work = HomeworkEditorWork.Attachment, error = null)) }
        viewModelScope.launch {
            var imported: ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile? = null
            var adopted = false
            try {
                val saved = writes.withLock { withContext(Dispatchers.IO) {
                    container.homeworkFiles.importUri(container.app, uri, editor.draft, kind, editor.files.count { !it.staged }).also { imported = it }
                } }
                mutable.update { state ->
                    val current = state.homeworkEditor?.takeIf { it.draft == editor.draft } ?: return@update state
                    state.copy(homeworkEditor = current.copy(files = current.files + saved))
                }
                adopted = mutable.value.homeworkEditor?.let { it.draft == editor.draft && it.files.any { file -> file.id == saved.id } } == true
            } catch (e: HomeworkFileException) {
                homeworkEditorError(editor.draft, container.app.getString(fileMessage(e.code)))
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "attach", e)
                homeworkEditorError(editor.draft, container.app.getString(R.string.hw_attach_bad))
            } finally {
                if (!adopted) imported?.let { file -> withContext(NonCancellable + Dispatchers.IO) {
                    runCatching { container.homeworkFiles.discardFile(editor.draft, file.id) }
                } }
                finishHomeworkEditorWork(editor.draft)
            }
        }
    }

    private fun removeHomeworkFile(id: String) {
        val editor = mutable.value.homeworkEditor ?: return
        if (editor.busy) return
        val file = editor.files.firstOrNull { it.id == id } ?: return
        mutable.update { it.copy(homeworkEditor = editor.copy(work = HomeworkEditorWork.Attachment, error = null)) }
        viewModelScope.launch {
            try {
                if (file.staged && editor.draft.isNotEmpty()) writes.withLock { withContext(Dispatchers.IO) { container.homeworkFiles.discardFile(editor.draft, id) } }
                mutable.update { state ->
                    val current = state.homeworkEditor?.takeIf { it.draft == editor.draft } ?: return@update state
                    state.copy(homeworkEditor = current.copy(files = current.files.filter { it.id != id },
                        missingFileIds = current.missingFileIds - id, removed = current.removed + id))
                }
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) { homeworkEditorError(editor.draft, container.app.getString(R.string.hw_attach_bad)) }
            finally { finishHomeworkEditorWork(editor.draft) }
        }
    }

    private fun attachHomeworkMany(kind: String, uris: List<Uri>) {
        if (uris.size == 1) { attachHomework(kind, uris.first()); return }
        val editor = mutable.value.homeworkEditor ?: return
        if (editor.draft.isEmpty() || editor.busy || uris.isEmpty()) return
        mutable.update { it.copy(homeworkEditor = editor.copy(work = HomeworkEditorWork.Attachment, error = null)) }
        viewModelScope.launch {
            try {
                val result = writes.withLock { withContext(Dispatchers.IO) {
                    ru.bgtu_voenmeh.zapara.ui.homework.importHomeworkAttachmentBatch(container,
                        editor, kind, uris) { saved ->
                        mutable.update { state ->
                            val current = state.homeworkEditor?.takeIf { it.draft == editor.draft } ?: return@update state
                            state.copy(homeworkEditor = current.copy(files = current.files + saved))
                        }
                        mutable.value.homeworkEditor?.let { it.draft == editor.draft &&
                            it.files.any { file -> file.id == saved.id } } == true
                    }
                } }
                if (result.failed > 0) homeworkEditorError(editor.draft, container.app.getString(
                    R.string.ux300_android_attachment_batch_result, result.added, result.failed))
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (_: Exception) { homeworkEditorError(editor.draft, container.app.getString(R.string.hw_attach_bad)) }
            finally { finishHomeworkEditorWork(editor.draft) }
        }
    }

    private fun loadHomeworkShareOptions() {
        val editor = mutable.value.homeworkEditor?.takeIf { it.share && !it.isEdit } ?: return
        if (editor.shareContext != null || editor.shareLoading) return
        val group = ctx?.groupId ?: return
        val profile = container.profile.databaseName
        val epoch = completionEpoch
        if (editor.scheduleGroupId != group) return
        mutable.update { s -> if (s.homeworkEditor?.draft == editor.draft) s.copy(homeworkEditor = s.homeworkEditor.copy(shareLoading = true, error = null)) else s }
        viewModelScope.launch {
            val result = try { withContext(Dispatchers.IO) { loadHomeworkShareContext(container, editor.scheduleGroupId) } }
            catch (e: CancellationException) { throw e }
            catch (_: Exception) { null }
            mutable.update { s ->
                val current = s.homeworkEditor
                if (current?.draft != editor.draft || !current.share) s
                else if (group == ctx?.groupId && epoch == completionEpoch && profile == container.profile.databaseName)
                    s.copy(homeworkEditor = current.copy(shareContext = result, shareLoading = false))
                else s.copy(homeworkEditor = current.copy(shareContext = null, shareLoading = false,
                    sourceChanged = true))
            }
        }
    }

    private fun retryHomeworkShare() {
        val editor = mutable.value.homeworkEditor ?: return
        val request = editor.shareRequest?.takeIf { it.operationId != null } ?: return
        if (editor.busy) return
        mutable.update { s -> if (s.homeworkEditor?.draft == editor.draft) s.copy(homeworkEditor = s.homeworkEditor.copy(work = HomeworkEditorWork.Saving, error = null)) else s }
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) { retryHomeworkPublication(container, request) }
                mutable.update { s -> if (s.homeworkEditor?.draft == editor.draft) s.copy(homeworkEditor = null) else s }
                container.toasts.show(container.app.getString(R.string.homework_share_selected_success), ToastKind.Ok)
            } catch (cancel: CancellationException) { throw cancel }
            catch (_: Exception) { homeworkEditorError(editor.draft, container.app.getString(R.string.homework_share_retry_failed)) }
            finally { finishHomeworkEditorWork(editor.draft) }
        }
    }

    private fun finishHomeworkEditorWork(draft: String) {
        mutable.update { current -> if (current.homeworkEditor?.draft == draft) current.copy(homeworkEditor = current.homeworkEditor.copy(work = HomeworkEditorWork.Idle)) else current }
    }

    private fun homeworkEditorError(draft: String, message: String) {
        mutable.update { current -> if (current.homeworkEditor?.draft == draft) current.copy(homeworkEditor = current.homeworkEditor.copy(error = message)) else current }
    }

    companion object {
        fun factory(container: AppContainer, dateArg: String?) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                ScheduleViewModel(container, dateArg) as T
        }
    }
}
