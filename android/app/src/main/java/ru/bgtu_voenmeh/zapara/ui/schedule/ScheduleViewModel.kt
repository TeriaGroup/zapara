package ru.bgtu_voenmeh.zapara.ui.schedule

import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
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
import ru.bgtu_voenmeh.zapara.ui.AppEvent
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.components.ToastKind
import ru.bgtu_voenmeh.zapara.ui.homework.fileMessage
import ru.bgtu_voenmeh.zapara.ui.friends.FriendPalette
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkEditorState
import ru.bgtu_voenmeh.zapara.ui.homework.shareSavedHomework
import java.time.LocalDate
import java.time.LocalDateTime

class ScheduleViewModel(
    private val container: AppContainer,
    private val initialDateArg: String?
) : ViewModel() {
    private val mutable = MutableStateFlow(ScheduleUiState(guest = container.profile.isGuest))
    val state: StateFlow<ScheduleUiState> = mutable.asStateFlow()
    private var ctx: SchedCtx? = null
    private var allLessons: List<Lesson> = emptyList()
    private val loadGate = Mutex()
    private val writes = Mutex()
    private val sharedCache = ru.bgtu_voenmeh.zapara.data.communities.SharedHomeworkCache()
    private val projection = ScheduleProjectionController(mutable)
    private val sharedHomework get() = projection.shared.rows
    private val sharedCompletion get() = projection.shared.completions
    private var sharedBusy = false
    private var undoSerial = 0L
    private fun expireUndo() {
        val serial = ++undoSerial
        viewModelScope.launch { delay(5_000); if (serial == undoSerial) mutable.update { it.copy(undoDone = null, undoShared = null) } }
    }
    private var savingHomework = false
    private var savingRename = false

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
                syncClock()
                mutable.update { it.copy(selected = event.date, undoDone = null, undoShared = null) }
                ensureAround(event.date)
            }
            ScheduleEvent.Retry -> viewModelScope.launch { bootstrap() }
            is ScheduleEvent.QuickDay -> viewModelScope.launch {
                syncClock()
                val date=container.clock().toLocalDate().plusDays(event.offset.toLong())
                mutable.update { it.copy(selected=date,undoDone=null,undoShared=null) }
                ensureAround(date)
            }
            ScheduleEvent.Today -> viewModelScope.launch {
                syncClock()
                val today = container.clock().toLocalDate()
                mutable.update { it.copy(today = today, selected = today) }
                ensureAround(today)
            }
            is ScheduleEvent.FreeTime -> {
                container.app.getSharedPreferences("day-planner", 0).edit().putBoolean("free-time", event.on).apply()
                mutable.update { it.copy(showFreeTime = event.on) }
            }
            ScheduleEvent.UndoShared -> mutable.value.undoShared?.let { (id, previous) -> toggleShared(id, previous, undo = true) }
            ScheduleEvent.UndoDone -> mutable.value.undoDone?.let { (id, previous) -> viewModelScope.launch {
                withContext(Dispatchers.IO) { container.homework.markDone(id, previous) }
                mutable.update { it.copy(undoDone = null, undoShared = null) }
                container.events.emit(AppEvent.PersonalizationChanged)
            } }
            ScheduleEvent.SyncClock -> syncClock()
            ScheduleEvent.RefreshShared -> viewModelScope.launch { ctx?.groupId?.let { loadShared(it) } }
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
            is ScheduleEvent.SubjectHomework -> viewModelScope.launch {
                val rows = withContext(Dispatchers.IO) { container.homework.forSubjectByNorm(event.lesson.subjectNorm).map { HomeworkRowUi(it.id, it.text, it.due?.toString() ?: container.app.getString(R.string.space_day_29), it.status, it.done) } }
                mutable.update { it.copy(actionsFor = null, subjectHomework = event.lesson, subjectRows = rows + sharedHomework.filter { hw -> hw.title.equals(event.lesson.subjectRaw, true) || hw.title.equals(event.lesson.name, true) }.map { hw -> sharedRow(hw) }) }
            }
            ScheduleEvent.CloseSubjectHomework -> mutable.update { it.copy(subjectHomework = null, sharedDetail = null) }
            is ScheduleEvent.OpenHomework -> openExistingHomework(event.row)
            is ScheduleEvent.ToggleShared -> toggleShared(event.id, event.done)
            is ScheduleEvent.ToggleDone -> toggleDone(event.id)
            is ScheduleEvent.AddHomework -> openHomework(event.lesson)
            is ScheduleEvent.HomeworkEditorText -> mutable.update { s ->
                s.copy(homeworkEditor = s.homeworkEditor?.withText(event.text))
            }
            is ScheduleEvent.HomeworkEditorShare -> mutable.update { s ->
                s.copy(homeworkEditor = s.homeworkEditor?.copy(share = event.on))
            }
            ScheduleEvent.HomeworkEditorInc -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.inc()) }
            ScheduleEvent.HomeworkEditorDec -> mutable.update { s -> s.copy(homeworkEditor = s.homeworkEditor?.dec()) }
            ScheduleEvent.RecalculateHomework -> viewModelScope.launch {
                val editor=mutable.value.homeworkEditor ?: return@launch
                val prepared=withContext(Dispatchers.IO) {
                    val prefs=container.repo.settings(); val gid=prefs.myGroupId.orEmpty()
                    val context=SchedCtx(gid,prefs.periodStart,prefs.weekCount,prefs.parityInvert)
                    val lessons=container.ownLessons(); val existing=editor.id?.let(container.homework::getById)
                    val anchor=existing?.createdAt ?: editor.creationAnchor(container.clock().toLocalDate())
                    val norm=ru.bgtu_voenmeh.zapara.data.Parity.normalizeSubject(editor.subjectRaw)
                    val due:(Int,String)->LocalDate? = { n,_ -> container.homework.dueDateIn({ g,dow,parity -> lessons.filter { it.groupId==g && it.dayOfWeek==dow && (it.parity==0 || it.parity==parity) } },context,norm,anchor,n) }
                    due to gid
                }
                mutable.update { state -> if(state.homeworkEditor?.draft==editor.draft) state.copy(homeworkEditor=editor.copy(dueFor=prepared.first,scheduleGroupId=prepared.second,sourceChanged=false)) else state }
            }
            ScheduleEvent.HomeworkEditorSave -> saveHomework()
            ScheduleEvent.HomeworkEditorCancel -> cancelHomework()
            is ScheduleEvent.HomeworkAttach -> attachHomework(event.kind, event.uri)
            is ScheduleEvent.HomeworkRemoveFile -> removeHomeworkFile(event.id)
            is ScheduleEvent.OpenMap -> { }
            is ScheduleEvent.PickSubgroup -> pickSubgroup(event.streamId, event.optionId)
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
            if (ctx?.groupId != gid) { sharedCache.clear(); projection.purge() }
            ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            allLessons = all
            val parsedArg = initialDateArg?.let { runCatching { LocalDate.parse(it) }.getOrNull() }
            val visible = ru.bgtu_voenmeh.zapara.data.Subgroups.visible(all, container.subgroupChoices(gid))
            val selected = if (mutable.value.loaded) mutable.value.selected else parsedArg ?: today
            mutable.update { it.copy(loaded = true, hasGroup = gid.isNotEmpty(), today = today, selected = selected, pages = emptyMap(), error = null, now = now, sourceStatus = ru.bgtu_voenmeh.zapara.ui.settings.SettingsLogic.updatedLine(prefs.lastFetchedAt, now, container.copy, allLessons.isNotEmpty()), showFreeTime = container.app.getSharedPreferences("day-planner", 0).getBoolean("free-time", true)) }
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
            if (ctx?.groupId != gid) { sharedCache.clear(); projection.purge() }
            ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
            allLessons = all
            val rolled = ScheduleComposer.syncToday(today, mutable.value.today, mutable.value.selected)
            val dates = (mutable.value.pages.keys + rolled.second).distinct()
            mutable.update { it.copy(hasGroup = gid.isNotEmpty(), today = rolled.first, selected = rolled.second, error = null, showFreeTime = container.app.getSharedPreferences("day-planner", 0).getBoolean("free-time", true)) }
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
        ensurePage(date.minusDays(1))
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
                        hint = if (hit != null && visibleScore < 0) "$baseHint · ${container.copy.get("friend_below_level")}" else baseHint
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
        return page.copy(lessons = if (source != null) emptyList() else page.lessons, deadlines = localDeadlines + sharedDeadlines, dataState = source)
    }

    private fun pickSubgroup(streamId: String, optionId: String) {
        val gid = ctx?.groupId?.takeIf { it.isNotEmpty() } ?: return
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                container.subgroups.select(container.profile.databaseName, gid, streamId, optionId)
            }
            container.events.emit(AppEvent.SubgroupChanged)
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
        viewModelScope.launch {
            val existing = withContext(Dispatchers.IO) {
                val has = container.overrides.all().any { it.subjectRawNormalized == lesson.subjectNorm }
                val note = container.overrides.noteByNorm(lesson.subjectNorm, lesson.dayOfWeek)
                has to note
            }
            mutable.update {
                it.copy(
                    actionsFor = null,
                    rename = RenameUi(
                        lesson = lesson, name = lesson.name, note = existing.second,
                        scope = 0, hasExisting = existing.first,
                        original = lesson.original ?: lesson.name,
                        dayName = Parity.dayNumberToTitle(lesson.dayOfWeek)
                    )
                )
            }
        }
    }

    private fun saveRename() {
        if (savingRename) return
        val ui = mutable.value.rename ?: return
        savingRename = true
        mutable.update { it.copy(rename = null) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        container.overrides.addOrUpdate(
                            ui.lesson.subjectRaw,
                            if (ui.scope == 0) "global" else "weekday:${ui.lesson.dayOfWeek}",
                            ui.name.trim(),
                            ui.note.trim().ifBlank { null }
                        )
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                mutable.update { cur -> if (cur.rename == null) cur.copy(rename = ui) else cur }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "rename", e)
                mutable.update { cur -> if (cur.rename == null) cur.copy(rename = ui) else cur }
            } finally {
                savingRename = false
            }
        }
    }

    private fun resetRename() {
        if (savingRename) return
        val ui = mutable.value.rename ?: return
        savingRename = true
        mutable.update { it.copy(rename = null) }
        viewModelScope.launch {
            try {
                writes.withLock {
                    withContext(Dispatchers.IO) {
                        container.overrides.all().filter { it.subjectRawNormalized == ui.lesson.subjectNorm }
                            .forEach { container.overrides.remove(it.id) }
                    }
                }
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                mutable.update { cur -> if (cur.rename == null) cur.copy(rename = ui) else cur }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "rename reset", e)
                mutable.update { cur -> if (cur.rename == null) cur.copy(rename = ui) else cur }
            } finally {
                savingRename = false
            }
        }
    }

    private fun sharedRow(hw: ru.bgtu_voenmeh.zapara.data.communities.CommunityHomework, completions: Map<String,ru.bgtu_voenmeh.zapara.data.communities.HomeworkCompletion> = projection.shared.completions) = HomeworkRowUi(
        0, hw.body, container.app.getString(R.string.space_day_35, (hw.title).toString(), (hw.deadlineAt?.atZone(java.time.ZoneId.systemDefault())?.toLocalDate() ?: container.app.getString(R.string.space_day_34)).toString()),
        if (hw.deadlineAt?.isBefore(java.time.Instant.now()) == true) "overdue" else "active", completions[hw.homeworkId]?.completed == true, hw.homeworkId)

    private fun purgeShared() {
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
            mutable.update { it.copy(subjectRows = it.subjectRows.mapNotNull { row ->
                if (row.sharedId == null) row else snapshot.rows.firstOrNull { hw -> hw.homeworkId == row.sharedId }?.let { hw -> sharedRow(hw) }
            }, sharedDetail = it.sharedDetail?.sharedId?.let { id -> snapshot.rows.firstOrNull { hw -> hw.homeworkId == id }?.let { hw -> sharedRow(hw) } }) }
        }
    }

    private fun completionScope() = ScheduleProjectionController.CompletionScope(
        (container.app as? ru.bgtu_voenmeh.zapara.ZaparaApplication)?.container?.profile ?: container.profile,
        ctx?.groupId, projection.shared.communityId)

    private fun toggleShared(id: String, done: Boolean, undo: Boolean = false) {
        if (sharedBusy) return
        val api = container.communities ?: return
        val operation=projection.shared
        val community = operation.communityId ?: return
        val group = ctx?.groupId ?: return
        val operationScope=ScheduleProjectionController.CompletionScope(container.profile,group,community)
        sharedBusy = true
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
                val purged=projection.completionDenied(operationScope,currentScope,e.failure,
                    invalidateCache={sharedCache.invalidate(group,community,operationToken)},purgeCurrent=::purgeShared)
                if(purged || operationScope==currentScope) container.toasts.show(container.app.getString(R.string.homework_save_failed), ToastKind.Bad)
            }
            catch (_: Exception) { if(operationScope==completionScope()) container.toasts.show(container.app.getString(R.string.homework_save_failed), ToastKind.Bad) }
            finally { sharedBusy = false }
        }
    }

    private fun openExistingHomework(row: HomeworkRowUi) {
        if (row.sharedId != null) {
            val current=projection.shared.rows.firstOrNull { it.homeworkId==row.sharedId } ?: return
            mutable.update { it.copy(subjectHomework=null,sharedDetail=sharedRow(current)) }
            return
        }
        viewModelScope.launch {
            val hw = withContext(Dispatchers.IO) { container.homework.all().firstOrNull { it.id == row.id } } ?: return@launch
            val files = withContext(Dispatchers.IO) { container.homeworkFiles.list(hw.id) }
            val lesson = mutable.value.subjectHomework
            val context = ctx
            val snapshot = allLessons
            val choices = context?.let { container.subgroupChoices(it.groupId) }.orEmpty()
            mutable.update { it.copy(subjectHomework = null, homeworkEditor = HomeworkEditorState(hw.id, lesson?.subjectRaw ?: hw.norm, lesson?.name ?: hw.norm, hw.text, hw.n, true,
                { n, _ -> if (context == null) hw.due else container.homework.dueDateIn({ gid, dow, parity -> ru.bgtu_voenmeh.zapara.data.HomeworkDue.lessonsOnChosenDay(snapshot.filter { row -> row.groupId == gid }, choices, dow, parity) }, context, hw.norm, hw.createdAt, n) }, files, java.util.UUID.randomUUID().toString())) }
        }
    }

    private fun toggleDone(id: Long) {
        viewModelScope.launch {
            writes.withLock {
                withContext(Dispatchers.IO) {
                    val hw = container.homework.all().firstOrNull { it.id == id } ?: return@withContext
                    container.homework.markDone(id, !hw.done)
                    mutable.update { it.copy(undoDone = id to hw.done, undoShared = null) }
                    expireUndo()
                }
            }
            container.events.emit(AppEvent.PersonalizationChanged)
        }
    }

    private fun openHomework(lesson: LessonUi) {
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
        mutable.update { it.copy(homeworkEditor = null) }
        viewModelScope.launch {
            try {
                val outcome = writes.withLock {
                    withContext(Dispatchers.IO) {
                        shareSavedHomework(container, editor) {
                            val id = if (editor.id == null) container.homework.addHomework(editor.subjectRaw, editor.text.trim(), editor.n, editor.creationAnchor(container.clock().toLocalDate()))
                            else {
                                container.homework.updateHomework(editor.id, editor.text.trim(), editor.n)
                                editor.id
                            }
                            if (editor.draft.isNotEmpty()) container.homeworkFiles.commit(editor.draft, id, editor.removed)
                        }
                    }
                }
                val note = outcome.note.ifBlank { container.app.getString(R.string.hw_saved) }
                container.toasts.show(note, ToastKind.Ok)
                container.events.emit(AppEvent.PersonalizationChanged)
            } catch (e: CancellationException) {
                mutable.update { cur -> if (cur.homeworkEditor == null) cur.copy(homeworkEditor = editor) else cur }
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "homework", e)
                mutable.update { cur -> if (cur.homeworkEditor == null) cur.copy(homeworkEditor = editor.copy(sourceChanged=e is ru.bgtu_voenmeh.zapara.ui.homework.HomeworkScheduleChanged)) else cur }
                container.toasts.show(container.app.getString(if (e is ru.bgtu_voenmeh.zapara.ui.homework.HomeworkScheduleChanged) R.string.review_homework_source_changed else R.string.homework_save_failed), ToastKind.Bad)
            } finally {
                savingHomework = false
            }
        }
    }

    private fun cancelHomework() {
        val editor = mutable.value.homeworkEditor
        mutable.update { it.copy(homeworkEditor = null) }
        if (editor != null && editor.draft.isNotEmpty()) {
            viewModelScope.launch(Dispatchers.IO) { container.homeworkFiles.discard(editor.draft) }
        }
    }

    private fun attachHomework(kind: String, uri: Uri) {
        val editor = mutable.value.homeworkEditor ?: return
        if (editor.draft.isEmpty()) return
        viewModelScope.launch {
            try {
                val saved = withContext(Dispatchers.IO) {
                    container.homeworkFiles.importUri(container.app, uri, editor.draft, kind, editor.files.count { !it.staged })
                }
                mutable.update { state ->
                    val current = state.homeworkEditor?.takeIf { it.draft == editor.draft } ?: return@update state
                    state.copy(homeworkEditor = current.copy(files = current.files + saved))
                }
            } catch (e: HomeworkFileException) {
                container.toasts.show(container.app.getString(fileMessage(e.code)), ToastKind.Bad)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaSchedule", "attach", e)
                container.toasts.show(container.app.getString(R.string.hw_attach_bad), ToastKind.Bad)
            }
        }
    }

    private fun removeHomeworkFile(id: String) {
        val editor = mutable.value.homeworkEditor ?: return
        val file = editor.files.firstOrNull { it.id == id } ?: return
        mutable.update { state ->
            val current = state.homeworkEditor ?: return@update state
            state.copy(homeworkEditor = current.copy(
                files = current.files.filter { it.id != id },
                removed = current.removed + id
            ))
        }
        if (file.staged && editor.draft.isNotEmpty()) {
            viewModelScope.launch(Dispatchers.IO) { container.homeworkFiles.discardFile(editor.draft, id) }
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
