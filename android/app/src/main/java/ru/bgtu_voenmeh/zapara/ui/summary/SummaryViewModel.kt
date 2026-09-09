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

class SummaryViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(SummaryUiState())
    val state: StateFlow<SummaryUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch { reload() }
        viewModelScope.launch { container.events.events.collect { reload() } }
    }

    fun onEvent(event: SummaryEvent) {
        when (event) {
            is SummaryEvent.Segment -> viewModelScope.launch {
                mutable.update { it.copy(segment = event.index) }
                reload()
            }
        }
    }

    private suspend fun reload() {
        try {
            val snap = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val gid = prefs.myGroupId.orEmpty()
                val lessons = if (gid.isEmpty()) emptyList() else container.repo.allForGroup(gid)
                val tiles = SummaryComposer.tiles(mutable.value.segment, lessons, { norm, dow ->
                    container.overrides.displayNameByNorm(norm, dow)
                }, container.copy)
                mutable.value.copy(loaded = true, hasGroup = gid.isNotEmpty(), tiles = tiles)
            }
            mutable.value = snap
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaSummary", "reload", e)
            mutable.update { it.copy(loaded = true) }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = SummaryViewModel(container) as T
        }
    }
}
