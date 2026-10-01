package ru.bgtu_voenmeh.zapara.ui.week

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

class WeekViewModel(private val container: AppContainer, private val initialDateArg: String? = null) : ViewModel() {
    private val mutable = MutableStateFlow(WeekUiState())
    val state: StateFlow<WeekUiState> = mutable.asStateFlow()
    private var reloadTicket = 0
    private var retryTicket = 0L
    private var anchorDate = initialDateArg?.let { runCatching { java.time.LocalDate.parse(it) }.getOrNull() }
    private var currentCtx: SchedCtx? = null
    private val parityHistory = WeekParityHistory()

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: WeekEvent) {
        when (event) {
            is WeekEvent.Parity -> {
                val context = currentCtx ?: return
                anchorDate = parityHistory.choose(mutable.value.selectedDate, event.index + 1, context)
                viewModelScope.launch { reload() }
            }
            is WeekEvent.OpenDay -> { }
            is WeekEvent.Shift -> {
                anchorDate = mutable.value.selectedDate.plusWeeks(event.weeks.toLong())
                currentCtx?.let { parityHistory.reset(anchorDate!!, it) }
                viewModelScope.launch { reload() }
            }
            WeekEvent.Today -> {
                anchorDate = container.clock().toLocalDate()
                currentCtx?.let { parityHistory.reset(anchorDate!!, it) }
                viewModelScope.launch { reload() }
            }
            WeekEvent.Retry -> retry()
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
                    if (!fetched) throw IllegalStateException("week refresh failed")
                }
                val currentGroup = withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() }
                if (!scope.matches(retryTicket, reloadTicket, container.profile.databaseName, currentGroup)) return@launch
                reload()
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaWeek", "retry", e)
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
        try {
            val (snap, context) = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                requestedGroup = gid
                val today = container.clock().toLocalDate()
                val current = if (Parity.isOddWeek(today, prefs.periodStart, prefs.weekCount, prefs.parityInvert)) 1 else 2
                val selectedDate = anchorDate ?: today
                val ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                val parity = WeekNavigation.parity(selectedDate, ctx)
                val lessons = if (gid.isEmpty()) emptyList() else container.ownLessons()
                val days = if (gid.isEmpty()) emptyList() else WeekComposer.compose(
                    parity, lessons,
                    { norm, dow -> container.overrides.displayNameByNorm(norm, dow) },
                    ctx, today, container.copy, selectedDate
                )
                WeekUiState(true, gid.isNotEmpty(), parity, current, days, selectedDate,
                    noSavedSchedule = gid.isNotEmpty() && lessons.isEmpty() && prefs.lastFetchedAt == null,
                    groupId = gid, profileName = owner) to ctx
            }
            if (ticket != reloadTicket || owner != container.profile.databaseName ||
                withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } != context.groupId) return
            if (currentCtx != context) parityHistory.reset(snap.selectedDate, context)
            currentCtx = context
            mutable.update { snap.copy(refreshing = it.refreshing) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaWeek", "reload", e)
            if (ticket != reloadTicket || owner != container.profile.databaseName) return
            val currentGroup = try { withContext(Dispatchers.IO) { container.repo.settings().myGroupId.orEmpty() } }
                catch (_: Exception) { requestedGroup }
            if (requestedGroup != null && currentGroup != requestedGroup) return
            mutable.update { state -> state.copy(loaded = true, error = container.app.getString(R.string.load_fail),
                groupId = currentGroup ?: state.groupId, profileName = owner,
                days = if (state.groupId == currentGroup) state.days else emptyList(),
                hasGroup = currentGroup?.isNotBlank() ?: state.hasGroup) }
        }
    }

    companion object {
        fun factory(container: AppContainer, dateArg: String? = null) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = WeekViewModel(container, dateArg) as T
        }
    }
}
