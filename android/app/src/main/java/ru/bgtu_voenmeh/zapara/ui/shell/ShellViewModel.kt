package ru.bgtu_voenmeh.zapara.ui.shell

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.room.InvalidationTracker
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

/** Guest shell projection: group chip, badges, theme, overlays. */
class ShellViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(ShellUiState())
    val state: StateFlow<ShellUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch {
            changes().collect { recompute() }
        }
        viewModelScope.launch {
            while (true) { delay(60_000); recompute() }
        }
        if (ru.bgtu_voenmeh.zapara.BuildConfig.SELF_UPDATE) {
            container.update.checkOnStart()
        }
    }

    private fun changes() = callbackFlow {
        val observer = object : InvalidationTracker.Observer("settings", "groups", "homework", "schedule_cache") {
            override fun onInvalidated(tables: Set<String>) { trySend(Unit) }
        }
        container.db.invalidationTracker.addObserver(observer)
        trySend(Unit)
        awaitClose { container.db.invalidationTracker.removeObserver(observer) }
    }.conflate().flowOn(Dispatchers.IO)

    private suspend fun recompute() {
        try {
            val projection = withContext(Dispatchers.IO) {
                val prefs = container.repo.settings()
                val groups = container.repo.groups()
                val now = container.clock()
                val homework = container.homework.all()
                ShellUiState(loaded = true, groupId = prefs.myGroupId,
                    groupName = groups.firstOrNull { it.id == prefs.myGroupId }?.name,
                    groups = groups,
                    odd = Parity.isOddWeek(now.toLocalDate(), prefs.periodStart, prefs.weekCount, prefs.parityInvert),
                    stale = ShellLogic.isStale(prefs.lastFetchedAt, now),
                    homeworkBadge = if (prefs.myGroupId.isNullOrEmpty()) 0 else ShellLogic.homeworkBadge(homework, now.toLocalDate()),
                    theme = ThemeChoice.fromKey(prefs.theme), animations = prefs.animations)
            }
            mutable.update { projection.copy(overlay = it.overlay) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaShell", "Guest projection failed", e)
            mutable.update { it.copy(loaded = true, error = true) }
        }
    }

    fun onEvent(event: ShellEvent) {
        when (event) {
            is ShellEvent.Overlay -> mutable.update { it.copy(overlay = event.value) }
            is ShellEvent.Theme -> save { it.copy(theme = event.value.key) }
            is ShellEvent.Animations -> save { it.copy(animations = event.enabled) }
            is ShellEvent.PickGroup -> viewModelScope.launch {
                try {
                    withContext(Dispatchers.IO) {
                        container.db.runInTransaction {
                            container.repo.saveSettings(container.repo.settings().copy(myGroupId = event.id))
                        }
                    }
                    container.events.emit(ru.bgtu_voenmeh.zapara.ui.AppEvent.GroupChanged)
                    val name = mutable.value.groups.firstOrNull { it.id == event.id }?.name ?: event.id
                    container.toasts.show(container.app.getString(ru.bgtu_voenmeh.zapara.R.string.toast_group, name), ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Ok)
                    mutable.update { it.copy(overlay = ShellOverlay.None) }
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) {
                    android.util.Log.w("ZaparaShell", "PickGroup failed", e)
                }
            }
        }
    }

    fun refresh() {
        viewModelScope.launch { recompute() }
    }

    private fun save(transform: (ScheduleRepository.SettingsState) -> ScheduleRepository.SettingsState) {
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) {
                    container.db.runInTransaction { container.repo.saveSettings(transform(container.repo.settings())) }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaShell", "Preference write failed", e)
                mutable.update { it.copy(error = true) }
            }
        }
    }

    companion object {
        fun factory(container: AppContainer) = object : ViewModelProvider.Factory {
            override fun <T : ViewModel> create(modelClass: Class<T>): T {
                require(modelClass == ShellViewModel::class.java)
                @Suppress("UNCHECKED_CAST")
                return ShellViewModel(container) as T
            }
        }
    }
}
