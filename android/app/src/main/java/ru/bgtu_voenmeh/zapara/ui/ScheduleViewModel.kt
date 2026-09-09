package ru.bgtu_voenmeh.zapara.ui

import android.app.Application
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.time.DayOfWeek
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.Friend
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.HomeworkService
import ru.bgtu_voenmeh.zapara.data.IntersectionService
import ru.bgtu_voenmeh.zapara.data.LecturerInfo
import ru.bgtu_voenmeh.zapara.data.LecturerLesson
import ru.bgtu_voenmeh.zapara.data.LecturerStore
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.MapInfo
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.MapStore
import ru.bgtu_voenmeh.zapara.data.Notifications
import ru.bgtu_voenmeh.zapara.data.OverrideService
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.PublicExport
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.Schedule
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import java.time.LocalDate
import java.time.format.TextStyle
import java.util.Locale

enum class Tab { Yesterday, Today, Tomorrow, Week, Summary }

data class SummarySection(
    val title: String,
    val total: Int,
    val byDay: List<Pair<String, Int>>,
    val byType: List<Pair<String, Int>>,
    val bySubject: List<Pair<String, Int>>,
    val byTeacher: List<Pair<String, Int>>,
    val roomsLine: String
)

data class ScheduleUiState(
    val tab: Tab = Tab.Tomorrow,
    /** Extra day shift from arrows (unbounded); effective date = tab base + offset. */
    val dayOffset: Int = 0,
    /** Last day tab (for return from Week/Summary). */
    val homeTab: Tab = Tab.Today,
    val weekParity: Int = 1, // 1 odd, 2 even for week view
    val groups: List<GroupInfo> = emptyList(),
    val groupId: String = "",
    val groupName: String = "—",
    val dateText: String = "—",
    val parityText: String = "—",
    val weekText: String = "",
    val     lessons: List<Lesson> = emptyList(),
    val allLessons: List<Lesson> = emptyList(),
    /** subjectNormalized -> "dd.MM eee" for current day rows (computed off-main-thread) */
    val nextMap: Map<String, String> = emptyMap(),
    /** traffic dots aligned with [lessons] order */
    val traffic: List<List<TrafficDot>> = emptyList(),
    /** subjectNormalized -> homework (far shown dimmed, not hidden) */
    val hwMap: Map<String, List<Homework>> = emptyMap(),
    /** "norm|dow" -> display name (precomputed off-main-thread) */
    val displayMap: Map<String, String> = emptyMap(),
    /** "norm|dow" -> note */
    val noteMap: Map<String, String> = emptyMap(),
    val friends: List<Friend> = emptyList(),
    val alwaysShow: Boolean = false,
    val invert: Boolean = false,
    val notifEnabled: Boolean = true,
    val notifTime1: String? = "20:00",
    val notifTime2: String? = "07:30",
    val dialog: UiDialog = UiDialog.None,
    // Maps (A4)
    val mapVisible: Boolean = false,
    val mapList: List<MapInfo> = emptyList(),
    val currentMap: MapInfo? = null,
    val mapPath: String? = null,
    val fullscreenMap: Boolean = false,
    // Teachers (A4)
    val teachersReady: Boolean = false,
    val teacherQuery: String = "",
    val teacherOnlyMy: Boolean = true,
    val teacherList: List<LecturerInfo> = emptyList(),
    val teacherSelected: LecturerInfo? = null,
    val teacherDetails: List<LecturerLesson> = emptyList(),
    val teacherMyIds: Set<String> = emptySet(),
    val teacherTotal: Int = 0,
    /** Teacher week filter: 0 = both parities, 1 = odd, 2 = even. */
    val teacherWeekParity: Int = 0,
    val summary: List<SummarySection> = emptyList(),
    val loading: Boolean = true,
    val error: String? = null
)

private val RU = Locale("ru")

class ScheduleViewModel(app: Application) : AndroidViewModel(app) {
    private val repo = ScheduleRepository.get(app)
    private val mapStore = MapStore(app)
    private val lecturerStore = LecturerStore(app)
    private val overrides = OverrideService(repo.db.overrideDao())
    private val homework = HomeworkService(
        repo.db.homeworkDao(),
        lessonsFor = { gid, dow, parity ->
            repo.allForGroup(gid).filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
        },
        ctx = {
            val st = repo.settings()
            SchedCtx(st.myGroupId.orEmpty(), st.periodStart, st.weekCount, st.parityInvert)
        }
    )
    var state by mutableStateOf(ScheduleUiState())
        private set
    var updateUi by mutableStateOf(UpdateUiState())
        private set
    private var dlJob: Job? = null
    @Volatile private var dlCancel = false
    private var firedFor: String? = null
    /** In-memory schedule context for main-thread-safe due previews (no DB access). */
    private var schedCtx: SchedCtx? = null

    init {
        viewModelScope.launch {
            try {
                android.util.Log.d("ZaparaApp", "init start")
                withContext(Dispatchers.IO) {
                    repo.ensureData()
                    val app = getApplication<Application>()
                    val cache = PublicExport.scheduleCache(app)
                    if ((!cache.exists() || cache.length() < 100) && ScheduleRepository.networkEnabled) {
                        try { repo.refresh() } catch (_: Exception) { PublicExport.ensure(app) }
                    } else {
                        PublicExport.ensure(app)
                    }
                }
                val groups = withContext(Dispatchers.IO) { repo.groups() }
                android.util.Log.d("ZaparaApp", "init groups=${groups.size}")
                val s = withContext(Dispatchers.IO) { repo.settings() }
                val gid = s.myGroupId?.takeIf { id -> groups.any { it.id == id } }
                    ?: groups.firstOrNull { it.id == "3313" }?.id // test group default
                    ?: groups.firstOrNull()?.id.orEmpty()
                // Smart start: today if lessons remain, else tomorrow.
                val startTab = withContext(Dispatchers.IO) { smartStartTab(gid) }
                render(startTab, gid, 1, groups, gid)
                android.util.Log.d("ZaparaApp", "init done tab=$startTab gid=$gid")
            } catch (e: Exception) {
                android.util.Log.d("ZaparaApp", "init error: $e")
                state = state.copy(loading = false, error = e.message ?: "load error")
            }
        }
        // Silent self-update check on launch (opt-out via Settings; off in RuStore flavor).
        viewModelScope.launch {
            if (!ru.bgtu_voenmeh.zapara.BuildConfig.SELF_UPDATE) return@launch
            try {
                val auto = withContext(Dispatchers.IO) {
                    AutoUpdate.isAutoUpdateEnabled(getApplication())
                }
                updateUi = updateUi.copy(auto = auto)
                if (!auto) return@launch
                val res = resolveLatest(force = false)
                val info = res.info
                if (info == null || res.upToDate) return@launch
                if (!AutoUpdate.isNewer(info.tag)) return@launch
                updateUi = updateUi.copy(tag = info.tag, apkUrl = info.apkUrl, htmlUrl = info.htmlUrl, hasUpdate = true)
                if (info.apkUrl != null) startUpdateDownload()
            } catch (_: Exception) {
            }
        }
        // Lecturer schedule loads in background (bundled asset, offline-first).
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) { lecturerStore.load() }
                refreshTeacherList()
            } catch (_: Exception) {
            }
        }
    }

    fun selectTab(tab: Tab) {
        if (state.loading) return
        // Direct tab tap resets the arrow shift; day tabs are remembered for return.
        state = state.copy(
            dayOffset = 0,
            homeTab = if (tab == Tab.Week || tab == Tab.Summary) state.homeTab else tab
        )
        viewModelScope.launch { render(tab, state.groupId, state.weekParity, state.groups, state.groupId) }
    }

    /** Arrow-stepping is unbounded: shift accumulates, the center button shows the full date. */
    fun stepDay(delta: Int) {
        if (state.loading) return
        val order = listOf(Tab.Yesterday, Tab.Today, Tab.Tomorrow)
        if (state.tab !in order) {
            val target = if (delta < 0) Tab.Yesterday else Tab.Tomorrow
            selectTab(target)
            return
        }
        state = state.copy(dayOffset = state.dayOffset + delta)
        viewModelScope.launch { render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId) }
    }

    /** Center button label: full day names near today, full date beyond. */
    fun dayCenterLabel(): String {
        val t = state.tab
        if (t == Tab.Week || t == Tab.Summary) return dayTabName(state.homeTab)
        val base = when (t) {
            Tab.Yesterday -> -1
            Tab.Tomorrow -> 1
            else -> 0
        }
        return when (val total = base + state.dayOffset) {
            -1 -> "Вчера"
            0 -> "Сегодня"
            1 -> "Завтра"
            else -> {
                val d = LocalDate.now().plusDays(total.toLong())
                "%02d.%02d · %s".format(
                    d.dayOfMonth, d.monthValue,
                    d.dayOfWeek.getDisplayName(TextStyle.SHORT, RU)
                )
            }
        }
    }

    private fun dayTabName(tab: Tab): String = when (tab) {
        Tab.Yesterday -> "Вчера"
        Tab.Today -> "Сегодня"
        Tab.Tomorrow -> "Завтра"
        Tab.Week -> "Неделя"
        Tab.Summary -> "Сводка"
    }

    fun selectWeekParity(parity: Int) {
        viewModelScope.launch { render(Tab.Week, state.groupId, parity, state.groups, state.groupId) }
    }

    fun selectGroup(groupId: String) {
        state = state.copy(dayOffset = 0, homeTab = Tab.Today)
        viewModelScope.launch {
            val s = withContext(Dispatchers.IO) { repo.settings() }
            withContext(Dispatchers.IO) { repo.saveSettings(s.copy(myGroupId = groupId)) }
            render(state.tab, groupId, state.weekParity, state.groups, groupId)
        }
    }

    fun refresh() {
        viewModelScope.launch {
            state = state.copy(loading = true, error = null)
            try {
                withContext(Dispatchers.IO) { repo.refresh() }
                val groups = withContext(Dispatchers.IO) { repo.groups() }
                render(state.tab, state.groupId, state.weekParity, groups, state.groupId)
            } catch (e: Exception) {
                state = state.copy(loading = false, error = e.message ?: "refresh error")
            }
        }
    }
    /** Re-render from current DB without network (used by tests). */
    fun reload() {
        android.util.Log.d("ZaparaApp", "reload() called")
        viewModelScope.launch {
            try {
                val groups = withContext(Dispatchers.IO) { repo.groups() }
                val s = withContext(Dispatchers.IO) { repo.settings() }
                val gid = state.groupId.ifEmpty {
                    s.myGroupId?.takeIf { id -> groups.any { it.id == id } }
                        ?: groups.firstOrNull { it.id == "3313" }?.id
                        ?: groups.firstOrNull()?.id.orEmpty()
                }
                render(state.tab, gid, state.weekParity, groups, gid)
            } catch (e: Exception) {
                state = state.copy(loading = false, error = e.message ?: "reload error")
            }
        }
    }

    private fun smartStartTab(groupId: String): Tab {
        return try {
            val today = LocalDate.now()
            val lessons = repo.lessonsFor(groupId, today)
            val last = lessons.maxByOrNull { it.timeEnd }
            val end = runCatching { java.time.LocalTime.parse(last?.timeEnd) }.getOrNull()
            if (last != null && end != null &&
                java.time.LocalTime.now().isBefore(end.plusMinutes(15))
            ) Tab.Today else Tab.Tomorrow
        } catch (_: Exception) {
            Tab.Tomorrow
        }
    }

    private suspend fun render(tab: Tab, groupId: String, weekParity: Int, groups: List<GroupInfo>, gid: String) {
        android.util.Log.d("ZaparaApp", "render start tab=$tab gid=$gid groups=${groups.size}")
        val s = withContext(Dispatchers.IO) { repo.settings() }
        val id = gid.ifEmpty { groups.firstOrNull()?.id.orEmpty() }
        // Persist the resolved group: fresh installs render a default group without saving it,
        // leaving settings.myGroupId null — homework due dates computed from an empty group (always null).
        if (id.isNotEmpty() && s.myGroupId != id) {
            withContext(Dispatchers.IO) { repo.saveSettings(s.copy(myGroupId = id)) }
        }
        schedCtx = SchedCtx(id, s.periodStart, s.weekCount, s.parityInvert)
        val name = groups.firstOrNull { it.id == id }?.name ?: "—"
        val dayBase = when (tab) {
            Tab.Yesterday -> LocalDate.now().minusDays(1)
            Tab.Today -> LocalDate.now()
            Tab.Tomorrow -> LocalDate.now().plusDays(1)
            else -> LocalDate.now()
        }
        // Arrow shift applies to day tabs only (Week/Summary ignore it).
        val date = if (tab == Tab.Week || tab == Tab.Summary) dayBase
        else dayBase.plusDays(state.dayOffset.toLong())
        val odd = Parity.isOddWeek(date, s.periodStart, s.weekCount, s.parityInvert)
        val dateText = "%02d.%02d.%d · %s".format(
            date.dayOfMonth, date.monthValue, date.year,
            date.dayOfWeek.getDisplayName(TextStyle.FULL, RU).replaceFirstChar { it.uppercase() }
        )
        val all = withContext(Dispatchers.IO) { repo.allForGroup(id) }
        val dayLessons = if (tab == Tab.Week || tab == Tab.Summary) emptyList()
        else Schedule.lessonsForDate(all, id, date, s.periodStart, s.weekCount, s.parityInvert)
        val summary = if (tab == Tab.Summary) withContext(Dispatchers.IO) { buildSummary(all) } else emptyList()
        val (traffic, hwMap) = withContext(Dispatchers.IO) {
            homework.recomputeAll()
            val dots = if (tab == Tab.Week || tab == Tab.Summary) emptyList()
            else dayLessons.map { l -> trafficFor(l, date, s) }
            // Show ALL homework including far (hiding far made new items "vanish").
            val hw = dayLessons
                .map { it.subjectNormalized }.distinct()
                .associateWith { norm ->
                    homework.forSubjectByNorm(norm)
                        .sortedBy { hwOrder(it.status) }
                }
            dots to hw
        }
        val (displayMap, noteMap) = withContext(Dispatchers.IO) {
            val keys = dayLessons.map { "${it.subjectNormalized}|${it.dayOfWeek}" }.distinct()
            // Store only non-empty display names so UI falls back to raw subject.
            keys.associateWith { key ->
                val norm = key.substringBefore("|")
                val dow = key.substringAfter("|").toIntOrNull() ?: 0
                overrides.displayNameByNorm(norm, dow)
            }.filterValues { it.isNotEmpty() } to keys.associateWith { key ->
                val norm = key.substringBefore("|")
                val dow = key.substringAfter("|").toIntOrNull() ?: 0
                overrides.noteByNorm(norm, dow)
            }
        }
        val nextMap = if (tab == Tab.Week || tab == Tab.Summary) emptyMap()
        else dayLessons.associate { l ->
            val d = Schedule.nextOccurrenceBySubject(
                all, id, l.subjectNormalized, date, s.periodStart, s.weekCount, s.parityInvert
            )
            l.subjectNormalized to if (d == null) "—" else "%02d.%02d %s".format(
                d.dayOfMonth, d.monthValue,
                d.dayOfWeek.getDisplayName(TextStyle.SHORT, RU)
            )
        }
        state = ScheduleUiState(
            tab = tab, weekParity = weekParity, groups = groups,
            groupId = id, groupName = name,
            dateText = if (tab == Tab.Week || tab == Tab.Summary) "" else dateText,
            parityText = if (odd) "НЕЧЕТНАЯ" else "ЧЕТНАЯ",
            weekText = "неделя ${Parity.weekNumber(date, s.periodStart)}",
            lessons = dayLessons,
            allLessons = all,
            nextMap = nextMap,
            traffic = traffic,
            hwMap = hwMap,
            displayMap = displayMap,
            noteMap = noteMap,
            friends = withContext(Dispatchers.IO) { repoFriends() },
            alwaysShow = s.alwaysShowAllTrafficLights,
            invert = s.parityInvert,
            notifEnabled = s.notifyEnabled,
            notifTime1 = s.notifyTime1,
            notifTime2 = s.notifyTime2,
            dialog = state.dialog,
            // Preserve arrow shift / return tab: render rebuilds the state from scratch.
            dayOffset = state.dayOffset,
            homeTab = state.homeTab,
            summary = summary, loading = false, error = null
        )
        android.util.Log.d(
            "ZaparaApp",
            "render done tab=$tab lessons=${dayLessons.size} groups=${groups.size}"
        )
    }

    private fun repoFriends(): List<Friend> =
        repo.db.friendDao().getAll().map {
            Friend(it.groupName, it.colorHex, it.enabled, it.memberNames)
        }

    private fun trafficFor(
        lesson: Lesson,
        date: LocalDate,
        s: ScheduleRepository.SettingsState
    ): List<TrafficDot> {
        val friends = repoFriends().filter { it.enabled }.take(5)
        if (friends.isEmpty()) return emptyList()
        val groups = repo.groups().associate { it.name to it.id }
        val inters = IntersectionService.intersections(
            my = lesson, date = date, friends = friends,
            strictness = s.intersectionStrictness,
            periodStart = s.periodStart, weekCount = s.weekCount, invert = s.parityInvert,
            lessonsFor = { fid, dow, parity ->
                repo.allForGroup(fid).filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
            },
            resolveId = { name -> groups[name] }
        )
        return if (s.alwaysShowAllTrafficLights) {
            val byGroup = inters.associateBy { it.friendGroupName }
            friends.map { f ->
                val hit = byGroup[f.groupName]
                if (hit != null) TrafficDot(f.groupName, f.memberNames, hit.score, hit.teacher, hit.room)
                else TrafficDot(f.groupName, f.memberNames, -1, "", "")
            }
        } else {
            inters.map { hit ->
                val f = friends.firstOrNull { it.groupName == hit.friendGroupName }
                TrafficDot(hit.friendGroupName, f?.memberNames.orEmpty(), hit.score, hit.teacher, hit.room)
            }
        }
    }

    private fun hwOrder(status: String): Int = when (status) {
        "burning_urgent" -> 0
        "burning" -> 1
        "overdue" -> 2
        "approaching" -> 3
        "far" -> 4
        "done" -> 5
        else -> 6
    }

    // ---- Dialog actions (all IO) ----

    fun openDialog(d: UiDialog) {
        state = state.copy(dialog = d)
    }

    fun openRename(lesson: Lesson) {
        viewModelScope.launch {
            val (name, note) = withContext(Dispatchers.IO) {
                overrides.displayName(lesson.subjectRaw, lesson.dayOfWeek) to
                    overrides.note(lesson.subjectRaw, lesson.dayOfWeek)
            }
            val idx = state.lessons.indexOf(lesson).takeIf { it >= 0 } ?: return@launch
            state = state.copy(dialog = UiDialog.Rename(idx, name, note))
        }
    }

    fun closeDialog() {
        state = state.copy(dialog = UiDialog.None)
    }

    fun displayName(lesson: Lesson): String =
        state.displayMap["${lesson.subjectNormalized}|${lesson.dayOfWeek}"] ?: lesson.subjectRaw

    fun displayNote(lesson: Lesson): String =
        state.noteMap["${lesson.subjectNormalized}|${lesson.dayOfWeek}"].orEmpty()

    fun saveRename(lesson: Lesson, displayName: String, note: String, global: Boolean) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                overrides.addOrUpdate(
                    lesson.subjectRaw,
                    if (global) "global" else "weekday:${lesson.dayOfWeek}",
                    displayName.ifBlank { lesson.subjectRaw }, note.ifBlank { null }
                )
            }
            closeDialog()
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun resetRename(lesson: Lesson) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val norm = Parity.normalizeSubject(lesson.subjectRaw)
                overrides.all().filter { it.subjectRawNormalized == norm }
                    .forEach { overrides.remove(it.id) }
            }
            closeDialog()
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun hwDuePreview(lesson: Lesson, n: Int): String {
        // In-memory only (main-thread safe): Room forbids main-thread queries.
        return try {
            val c = schedCtx ?: return "Срок: —"
            val mem = state.allLessons
            val due = homework.dueDateIn(
                { gid, dow, parity ->
                    mem.filter { it.groupId == gid && it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
                },
                c,
                Parity.normalizeSubject(lesson.subjectRaw), LocalDate.now(), n.coerceIn(1, 10)
            )
            if (due == null) "Срок: — (нет занятий)"
            else "Срок: %02d.%02d.%d".format(due.dayOfMonth, due.monthValue, due.year)
        } catch (_: Exception) {
            "Срок: —"
        }
    }

    fun saveHomework(lesson: Lesson, text: String, n: Int) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                homework.addHomework(lesson.subjectRaw, text, n, LocalDate.now())
            }
            closeDialog()
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun updateHomework(id: Long, text: String, n: Int) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { homework.updateHomework(id, text, n) }
            closeDialog()
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun toggleHomework(id: Long, done: Boolean) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { homework.markDone(id, done) }
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun deleteHomework(id: Long) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) { homework.delete(id) }
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun addFriend(groupName: String) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val existing = repo.db.friendDao().getAll()
                if (existing.size >= 5 || existing.any { it.groupName == groupName }) return@withContext
                val colors = listOf("#FF6CA5E0", "#FF98C379", "#FFE06C75", "#FFC678DD", "#FFF2C55C")
                repo.db.friendDao().insert(
                    ru.bgtu_voenmeh.zapara.data.db.FriendEntity(
                        groupName = groupName,
                        colorHex = colors[existing.size % colors.size],
                        enabled = true
                    )
                )
            }
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun removeFriend(friend: Friend) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val e = repo.db.friendDao().getAll().firstOrNull { it.groupName == friend.groupName }
                if (e != null) repo.db.friendDao().delete(e.id)
            }
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun saveMemberNames(friend: Friend, names: String) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val e = repo.db.friendDao().getAll().firstOrNull { it.groupName == friend.groupName }
                if (e != null && e.memberNames != names) {
                    repo.db.friendDao().update(e.copy(memberNames = names))
                }
            }
            // light refresh (no full render to keep typing smooth — caller re-renders on focus loss)
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun toggleAlwaysShow(value: Boolean) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val st = repo.settings()
                repo.saveSettings(st.copy(alwaysShowAllTrafficLights = value))
            }
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun toggleInvert(value: Boolean) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val st = repo.settings()
                repo.saveSettings(st.copy(parityInvert = value))
            }
            render(state.tab, state.groupId, state.weekParity, state.groups, state.groupId)
        }
    }

    fun setNotifEnabled(value: Boolean) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val st = repo.settings()
                repo.saveSettings(st.copy(notifyEnabled = value))
                val app = getApplication<Application>()
                if (value) Notifications.schedule(app) else Notifications.cancel(app)
            }
            state = state.copy(notifEnabled = value)
        }
    }

    fun saveNotifTimes(t1: String, t2: String) {
        viewModelScope.launch {
            withContext(Dispatchers.IO) {
                val st = repo.settings()
                repo.saveSettings(st.copy(notifyTime1 = t1.ifBlank { null }, notifyTime2 = t2.ifBlank { null }))
                Notifications.schedule(getApplication())
            }
            state = state.copy(notifTime1 = t1.ifBlank { null }, notifTime2 = t2.ifBlank { null })
        }
    }

    /** Fire a test notification for today (same content as the morning alarm). */
    fun testNotification() {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                Notifications.ensureChannel(getApplication())
                Notifications.showForTime(getApplication(), "__test__")
            } catch (_: Exception) {
            }
        }
    }

    fun notifPermissionGranted(): Boolean {
        if (android.os.Build.VERSION.SDK_INT < 33) return true
        return androidx.core.content.ContextCompat.checkSelfPermission(
            getApplication(),
            android.Manifest.permission.POST_NOTIFICATIONS
        ) == android.content.pm.PackageManager.PERMISSION_GRANTED
    }

    fun canScheduleExact(): Boolean {
        val am = getApplication<Application>().getSystemService(android.content.Context.ALARM_SERVICE) as android.app.AlarmManager
        return android.os.Build.VERSION.SDK_INT < 31 || am.canScheduleExactAlarms()
    }

    // ---- Self-update (GitHub releases) ----

    fun setAutoUpdate(v: Boolean) {
        viewModelScope.launch(Dispatchers.IO) {
            try { AutoUpdate.setAutoUpdateEnabled(getApplication(), v) } catch (_: Exception) {}
        }
        updateUi = updateUi.copy(auto = v)
    }

    private data class CheckResult(val info: AutoUpdate.UpdateInfo?, val upToDate: Boolean)

    /**
     * Release lookup with a 6h cache (shared-VPN IPs burn through GitHub's 60/hr anon quota).
     * force=true always hits the network (explicit "Проверить обновление" button).
     */
    private suspend fun resolveLatest(force: Boolean): CheckResult =
        withContext(Dispatchers.IO) {
            val app = getApplication<Application>()
            if (!force) {
                val c = AutoUpdate.cachedCheck(app)
                if (c.tag != null && System.currentTimeMillis() - c.at < AutoUpdate.CHECK_TTL_MS) {
                    val cached = AutoUpdate.UpdateInfo(c.tag, c.htmlUrl.orEmpty(), c.apkUrl, "")
                    return@withContext CheckResult(cached, !AutoUpdate.isNewer(c.tag))
                }
            }
            // Feed first (no quota), API fallback (exact URLs). Throws only if both fail.
            val info = AutoUpdate.getLatestSmart("android")
            if (info != null) AutoUpdate.saveCheck(app, info.tag, info.apkUrl, info.htmlUrl)
            CheckResult(info, info == null || !AutoUpdate.isNewer(info.tag))
        }

    private fun stamp(): String = try {
        java.time.LocalTime.now().format(java.time.format.DateTimeFormatter.ofPattern("HH:mm"))
    } catch (_: Exception) {
        ""
    }

    fun checkUpdateManual(force: Boolean = true) {
        if (updateUi.checking || updateUi.downloading) return
        viewModelScope.launch {
            updateUi = updateUi.copy(checking = true, error = null, upToDate = false, hasUpdate = false, tag = "", readyFile = null, log = "Запрос к GitHub...")
            try {
                val res = resolveLatest(force)
                val info = res.info
                val at = stamp()
                if (info == null) {
                    updateUi = updateUi.copy(checking = false, error = "Релизов нет", log = "API ок, релизов нет", checkedAt = at, htmlUrl = AutoUpdate.RELEASES_PAGE)
                } else if (!res.upToDate) {
                    updateUi = updateUi.copy(
                        checking = false, tag = info.tag,
                        apkUrl = info.apkUrl, htmlUrl = info.htmlUrl, hasUpdate = true,
                        log = "Найдено ${info.tag}", checkedAt = at
                    )
                } else {
                    updateUi = updateUi.copy(checking = false, upToDate = true, tag = info.tag, log = "Новее нет", checkedAt = at)
                }
            } catch (e: Exception) {
                val raw = e.message ?: e.javaClass.simpleName
                val friendly = if ("403" in raw) {
                    "GitHub ограничил запросы с вашей сети (VPN делит лимит). Попробуйте позже/без VPN — или кнопкой «В браузере»"
                } else {
                    "Ошибка: $raw"
                }
                updateUi = updateUi.copy(checking = false, error = friendly, log = "Проверка не удалась", checkedAt = stamp(), htmlUrl = AutoUpdate.RELEASES_PAGE)
            }
        }
    }

    fun startUpdateDownload() {
        val tag = updateUi.tag
        val url = updateUi.apkUrl
        if (tag.isEmpty() || url == null || updateUi.downloading) return
        dlJob?.cancel()
        dlJob = viewModelScope.launch {
            updateUi = updateUi.copy(downloading = true, progress = -1f, error = null, log = "Соединение...")
            dlCancel = false
            try {
                val app = getApplication<Application>()
                val dest = AutoUpdate.apkFileFor(app, tag)
                if (!dest.exists()) {
                    val main = android.os.Handler(android.os.Looper.getMainLooper())
                    var lastEmit = 0L
                    withContext(Dispatchers.IO) {
                        AutoUpdate.downloadAsset(
                            url, dest,
                            onProgress = { done, total ->
                                val now = android.os.SystemClock.uptimeMillis()
                                if (now - lastEmit > 250) {
                                    lastEmit = now
                                    val p = if (total > 0) done.toFloat() / total else -1f
                                    main.post {
                                        updateUi = updateUi.copy(progress = p, doneBytes = done, totalBytes = total, log = "Качаю...")
                                    }
                                }
                            },
                            isCancelled = { dlCancel }
                        )
                    }
                    if (dlCancel) {
                        updateUi = updateUi.copy(downloading = false, log = "Отменено")
                        return@launch
                    }
                } else {
                    updateUi = updateUi.copy(log = "Уже скачано")
                }
                updateUi = updateUi.copy(downloading = false, progress = 1f, readyFile = dest.absolutePath, log = "Скачано, открываю установщик...")
                fireInstaller(dest)
            } catch (_: CancellationException) {
                updateUi = updateUi.copy(downloading = false, log = "Отменено")
            } catch (_: AutoUpdate.DownloadCancelled) {
                updateUi = updateUi.copy(downloading = false, log = "Отменено")
            } catch (e: Exception) {
                updateUi = updateUi.copy(downloading = false, error = "Скачивание: ${e.message ?: e.javaClass.simpleName}", log = "Скачивание не удалось")
            }
        }
    }

    fun cancelUpdateDownload() {
        dlCancel = true
        dlJob?.cancel()
        updateUi = updateUi.copy(downloading = false)
    }

    fun dismissUpdate() {
        updateUi = UpdateUiState(auto = updateUi.auto)
        firedFor = null
    }

    /** Open the system installer for a downloaded APK (one user tap on Install is required by the OS). */
    fun fireInstaller(file: File) {
        try {
            if (firedFor == file.absolutePath) return
            firedFor = file.absolutePath
            val app = getApplication<Application>()
            app.startActivity(AutoUpdate.installIntent(app, file))
            updateUi = updateUi.copy(log = "Установщик открыт")
        } catch (e: Exception) {
            updateUi = updateUi.copy(error = "Установщик: ${e.message ?: e.javaClass.simpleName}", log = "Установщик не открылся")
        }
    }

    /** Manual "Установить" button: same, but failures stay visible in the dialog. */
    fun installReady() {
        val path = updateUi.readyFile
        if (path == null) {
            updateUi = updateUi.copy(error = "Нет скачанного файла")
            return
        }
        val file = File(path)
        if (!file.exists()) {
            updateUi = updateUi.copy(error = "Файл пропал, скачайте заново", readyFile = null)
            firedFor = null
            return
        }
        firedFor = null // allow explicit retry
        fireInstaller(file)
    }

    // ---- Maps (A4) ----

    private fun allMaps(): List<MapInfo> =
        MapResolve.MAP_FILES.entries
            .sortedWith(compareBy({ it.key.first }, { it.key.second }))
            .map { (k, f) ->
                MapInfo(
                    building = k.first, floor = k.second,
                    title = "${k.first} ${k.second} этаж", fileName = f,
                    roomRaw = "", classroomRaw = "",
                    isRemote = false, hasMap = true
                )
            }

    fun toggleMap() {
        val show = !state.mapVisible
        if (show && state.mapList.isEmpty()) {
            viewModelScope.launch {
                val list = withContext(Dispatchers.IO) { allMaps() }
                state = state.copy(mapVisible = true, mapList = list)
                if (state.currentMap == null) updateMapForNextLesson()
            }
        } else {
            state = state.copy(mapVisible = show)
        }
    }

    /** All 9 building maps as (fileName to title) pairs for the chooser dialog. */
    fun allMapPairs(): List<Pair<String, String>> =
        allMaps().map { it.fileName to it.title }

    /** Sorted floors available for a building (ГК 1-4, УЛК 1-5). */
    fun floorsForBuilding(building: String): List<Int> =
        allMaps().filter { it.building == building }.map { it.floor }.sorted()

    /** Step one floor up/down within the current building (no-op at the edge). */
    fun mapFloorStep(delta: Int) {
        val cur = state.currentMap
        if (cur == null || !cur.hasMap) return
        val floors = floorsForBuilding(cur.building)
        val next = floors.getOrNull(floors.indexOf(cur.floor) + delta) ?: return
        allMaps().firstOrNull { it.building == cur.building && it.floor == next }
            ?.let { selectMapByFile(it.fileName) }
    }

    /** Show a map chosen by building (header "Карта" button). */
    fun selectMapByFile(fileName: String) {
        val m = allMaps().firstOrNull { it.fileName == fileName } ?: return
        viewModelScope.launch {
            val path = withContext(Dispatchers.IO) {
                mapStore.mapFile(m.fileName)?.absolutePath
            }
            val list = if (state.mapList.isEmpty()) allMaps() else state.mapList
            state = state.copy(mapVisible = true, mapList = list, currentMap = m, mapPath = path)
        }
    }

    fun showMapFor(lesson: Lesson) {
        val mi = MapResolve.resolve(lesson.classroomRaw) ?: return
        viewModelScope.launch {
            val path = withContext(Dispatchers.IO) {
                if (mi.hasMap) mapStore.mapFile(mi.fileName)?.absolutePath else null
            }
            val list = if (state.mapList.isEmpty()) withContext(Dispatchers.IO) { allMaps() } else state.mapList
            state = state.copy(mapVisible = true, mapList = list, currentMap = mi, mapPath = path)
        }
    }

    fun setFullscreen(value: Boolean) {
        state = state.copy(fullscreenMap = value)
    }

    private fun updateMapForNextLesson() {
        viewModelScope.launch {
            try {
                val gid = state.groupId.ifEmpty { return@launch }
                val (lesson, _) = withContext(Dispatchers.IO) {
                    val s = repo.settings()
                    val all = repo.allForGroup(gid)
                    // Next lesson from now (today remaining, then following days).
                    val now = java.time.LocalTime.now()
                    val today = LocalDate.now()
                    for (offset in 0..6) {
                        val date = today.plusDays(offset.toLong())
                        if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
                        var code = Parity.weekCode(date, s.periodStart, s.weekCount)
                        if (s.parityInvert) code = if (code == 1) 2 else 1
                        val dow = date.dayOfWeek.value
                        val dayLessons = all.filter {
                            it.groupId == gid && it.dayOfWeek == dow && (it.parity == code || it.parity == 0)
                        }.sortedBy { it.timeStart }
                        for (l in dayLessons) {
                            if (offset == 0) {
                                val ts = runCatching { java.time.LocalTime.parse(l.timeStart) }.getOrNull()
                                    ?: continue
                                val te = runCatching { java.time.LocalTime.parse(l.timeEnd) }.getOrNull()
                                if (ts.isAfter(now) || (te != null && te.isAfter(now))) return@withContext l to date
                            } else {
                                return@withContext l to date
                            }
                        }
                    }
                    null to today
                }
                if (lesson != null) showMapFor(lesson)
            } catch (_: Exception) {
            }
        }
    }

    // ---- Teachers (A4) ----

    fun openTeachers() {
        state = state.copy(dialog = UiDialog.Teachers)
        refreshTeacherList()
    }

    fun teacherQuery(q: String) {
        state = state.copy(teacherQuery = q)
        refreshTeacherList()
    }

    fun teacherOnlyMy(value: Boolean) {
        state = state.copy(teacherOnlyMy = value)
        refreshTeacherList()
    }

    fun selectTeacher(t: LecturerInfo) {
        viewModelScope.launch {
            val details = withContext(Dispatchers.IO) { lecturerStore.lessonsFor(t.id) }
            state = state.copy(teacherSelected = t, teacherDetails = details)
        }
    }

    fun deselectTeacher() {
        state = state.copy(teacherSelected = null, teacherDetails = emptyList())
    }

    fun teacherWeekParity(parity: Int) {
        state = state.copy(teacherWeekParity = parity.coerceIn(0, 2))
    }

    fun isMyTeacher(t: LecturerInfo): Boolean {
        val ids = state.teacherMyIds
        return t.id in ids || t.name in ids
    }

    private fun myTeacherIds(): Set<String> {
        return try {
            lecturerStore.myTeacherIds(repo.allForGroup(state.groupId))
        } catch (_: Exception) {
            emptySet()
        }
    }

    private fun refreshTeacherList() {
        viewModelScope.launch {
            val ready = withContext(Dispatchers.IO) { lecturerStore.isLoaded() }
            if (!ready) {
                state = state.copy(teachersReady = false)
                return@launch
            }
            val q = state.teacherQuery
            val onlyMy = state.teacherOnlyMy
            val (list, sel, ids) = withContext(Dispatchers.IO) {
                val ids = myTeacherIds()
                val found = lecturerStore.search(q, onlyMy, ids)
                val kept = state.teacherSelected?.takeIf { s -> found.any { it.id == s.id } }
                Triple(found, kept, ids)
            }
            val details = if (sel != null) withContext(Dispatchers.IO) { lecturerStore.lessonsFor(sel.id) } else emptyList()
            val total = lecturerStore.lecturers().size
            state = state.copy(
                teachersReady = true, teacherList = list,
                teacherSelected = sel, teacherDetails = details, teacherMyIds = ids,
                teacherTotal = total
            )
        }
    }

    private suspend fun buildSummary(all: List<Lesson>): List<SummarySection> {
        // Resolve override display names off-main-thread.
        val displayOf: (Lesson) -> String = { l ->
            overrides.displayNameByNorm(l.subjectNormalized, l.dayOfWeek).ifEmpty { l.subjectRaw }
        }
        fun section(title: String, list: List<Lesson>): SummarySection {
            val byDay = list.groupBy { it.dayOfWeek }.toSortedMap()
                .map { (d, v) -> Parity.dayNumberToTitle(d).take(2) to v.size }
            val byType = list.groupBy { it.typeRaw.ifBlank { "—" } }
                .map { (k, v) -> k to v.size }.sortedByDescending { it.second }
            val bySubject = list.groupBy { l -> displayOf(l).ifBlank { "—" } }
                .map { (k, v) -> k to v.size }.sortedByDescending { it.second }
            val byTeacher = list.filter { it.teacherRaw.isNotBlank() && it.teacherRaw != "—" }
                .flatMap { l -> l.teacherRaw.split(";").map { it.trim() }.filter { it.isNotEmpty() } }
                .groupingBy { it }.eachCount().toList().sortedByDescending { it.second }
            val rooms = list.filter { it.classroomRaw.isNotBlank() }
                .groupingBy { it.classroomRaw.trimEnd(';', ' ') }.eachCount()
                .toList().sortedByDescending { it.second }
                .joinToString(" · ") { "${it.first} ${it.second}" }
            return SummarySection(title, list.size, byDay, byType, bySubject, byTeacher, rooms)
        }
        val odd = all.filter { it.parity == 1 }
        val even = all.filter { it.parity == 2 }
        return listOf(
            section("Нечетная", odd),
            section("Четная", even),
            section("Обе недели", all)
        )
    }

    fun weekLessons(dow: Int, parity: Int): List<Lesson> {
        return state.allLessons
            .filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
            .sortedBy { it.timeStart }
    }

    fun nextPairText(lesson: Lesson, from: LocalDate): String {
        // Legacy helper (kept for tests of pure logic in Schedule.*). UI uses state.nextMap.
        return state.nextMap[lesson.subjectNormalized] ?: "—"
    }

    @Suppress("unused")
    fun currentDateForTab(): LocalDate = when (state.tab) {
        Tab.Yesterday -> LocalDate.now().minusDays(1)
        Tab.Today -> LocalDate.now()
        Tab.Tomorrow -> LocalDate.now().plusDays(1)
        else -> LocalDate.now()
    }
}

class ScheduleVmFactory(private val app: Application) : ViewModelProvider.Factory {
    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T {
        return ScheduleViewModel(app) as T
    }
}
