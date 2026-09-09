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
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx

class WeekViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(WeekUiState())
    val state: StateFlow<WeekUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: WeekEvent) {
        when (event) {
            is WeekEvent.Parity -> viewModelScope.launch { reload(event.index + 1) }
            is WeekEvent.OpenDay -> { }
        }
    }

    private suspend fun reload(parityOverride: Int? = null) {
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                val today = container.clock().toLocalDate()
                val current = if (Parity.isOddWeek(today, prefs.periodStart, prefs.weekCount, prefs.parityInvert)) 1 else 2
                val parity = parityOverride ?: mutable.value.parity.takeIf { mutable.value.loaded } ?: current
                val ctx = SchedCtx(gid, prefs.periodStart, prefs.weekCount, prefs.parityInvert)
                val lessons = if (gid.isEmpty()) emptyList() else container.repo.allForGroup(gid)
                val days = if (gid.isEmpty()) emptyList() else WeekComposer.compose(
                    parity, lessons,
                    { norm, dow -> container.overrides.displayNameByNorm(norm, dow) },
                    ctx, today, container.copy
                )
                WeekUiState(true, gid.isNotEmpty(), parity, current, days)
            }
            mutable.value = snap
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaWeek", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = WeekViewModel(container) as T
        }
    }
}
