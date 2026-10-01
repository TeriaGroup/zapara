package ru.bgtu_voenmeh.zapara.ui.summary

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
import ru.bgtu_voenmeh.zapara.ui.schedule.AcademicRetryScope

class SummaryViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(SummaryUiState())
    val state: StateFlow<SummaryUiState> = mutable.asStateFlow()
    private var reloadTicket = 0
    private var retryTicket = 0L
    private var segmentChosen = false

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: SummaryEvent) {
        when (event) {
            is SummaryEvent.Segment -> viewModelScope.launch {
                segmentChosen = true
                mutable.update { it.copy(segment = event.index) }
                reload()
            }
            SummaryEvent.Retry -> retry()
        }
    }

    private fun retry() {
        if (mutable.value.refreshing) return
        val request = ++retryTicket
        val owner = container.profile.databaseName
        val group = mutable.value.groupId
        val scope = AcademicRetryScope(request, reloadTicket, owner, group)
        val fetchMissingSource = mutable.value.noSavedSchedule
        mutable.update { it.copy(refreshing = true, error = null) }
        viewModelScope.launch {
            try {
                if (fetchMissingSource) {
                    val fetched = withContext(Dispatchers.IO) { container.timetable.pull() }
                    if (!fetched) throw IllegalStateException("summary refresh failed")
                }
                val currentGroup = withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() }
                if (!scope.matches(retryTicket, reloadTicket, container.profile.databaseName, currentGroup)) return@launch
                reload()
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaSummary", "retry", e)
                val currentGroup = try { withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } }
                    catch (_: Exception) { group }
                if (scope.matches(retryTicket, reloadTicket, container.profile.databaseName, currentGroup))
                    mutable.update { it.copy(error = container.app.getString(R.string.load_fail)) }
            } finally { if (request == retryTicket) mutable.update { it.copy(refreshing = false) } }
        }
    }

    private suspend fun reload() {
        val ticket = ++reloadTicket
        val owner = container.profile.databaseName
        var requestedGroup: String? = null
        val chosen = segmentChosen
        val chosenSegment = mutable.value.segment
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                requestedGroup = gid
                val today = container.clock().toLocalDate()
                val ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                val segment = if (chosen) chosenSegment else if (Parity.isOddWeek(today,
                    prefs.periodStart, prefs.weekCount, prefs.parityInvert)) 0 else 1
                val lessons = if (gid.isEmpty()) emptyList() else container.ownLessons()
                val catalog = try { container.lecturerStore.load().lecturers }
                    catch (e: CancellationException) { throw e }
                    catch (_: Exception) { emptyList() }
                val groupName = container.repo.groups().firstOrNull { it.id == gid }?.name
                val identity = SummaryTeacherIdentity(catalog, container.lecturerStore::lessonsFor,
                    gid, groupName)
                val tiles = SummaryComposer.tiles(segment, lessons, { norm, dow ->
                    container.overrides.displayNameByNorm(norm, dow)
                }, container.copy, identity::resolve)
                SummaryUiState(loaded = true, hasGroup = gid.isNotEmpty(), segment = segment, tiles = tiles,
                    dayDates = tiles.byDay.mapNotNull { (day, _) -> SummaryDayNavigation.date(day, segment, today, ctx)?.let { day to it } }.toMap(),
                    noSavedSchedule = gid.isNotEmpty() && lessons.isEmpty() && prefs.lastFetchedAt == null,
                    groupId = gid, profileName = owner)
            }
            if (ticket != reloadTicket || owner != container.profile.databaseName ||
                withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } != snap.groupId) return
            mutable.update { snap.copy(refreshing = it.refreshing) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSummary", "reload", e)
            if (ticket != reloadTicket || owner != container.profile.databaseName) return
            val currentGroup = try { withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } }
                catch (_: Exception) { requestedGroup }
            if (requestedGroup != null && currentGroup != requestedGroup) return
            mutable.update { state -> state.copy(loaded = true, error = container.app.getString(R.string.load_fail),
                groupId = currentGroup ?: state.groupId, profileName = owner,
                tiles = if (state.groupId == currentGroup) state.tiles else SummaryTiles(0, emptyList(), emptyList(), emptyList(), emptyList()),
                dayDates = if (state.groupId == currentGroup) state.dayDates else emptyMap(),
                hasGroup = currentGroup?.isNotBlank() ?: state.hasGroup) }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = SummaryViewModel(container) as T
        }
    }
}
