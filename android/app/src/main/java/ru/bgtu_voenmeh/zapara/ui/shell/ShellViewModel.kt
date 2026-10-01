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
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetUpdater

/** Guest shell projection: group chip, badges, theme, overlays. */
class ShellViewModel(private val container: AppContainer) : ViewModel() {
    private val mutable = MutableStateFlow(ShellUiState())
    val state: StateFlow<ShellUiState> = mutable.asStateFlow()
    private val projectionGate = ShellProjectionGate()

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
        val request = projectionGate.begin()
        val owner = container.profile.databaseName
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
            val savedGroup = withContext(Dispatchers.IO) { container.repo.settings().myGroupId }
            if (!projectionGate.mayPublish(request, projection.groupId, savedGroup,
                    !container.closed && container.profile.databaseName == owner)) return
            mutable.update { projection.copy(overlay = it.overlay,
                groupPickPending = it.groupPickPending, pendingGroupId = it.pendingGroupId,
                groupPickError = it.groupPickError) }
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            android.util.Log.w("ZaparaShell", "Guest projection failed", e)
            if (projectionGate.current(request) && !container.closed && container.profile.databaseName == owner)
                mutable.update { it.copy(loaded = true, error = true) }
        }
    }

    fun onEvent(event: ShellEvent) {
        when (event) {
            is ShellEvent.Overlay -> mutable.update { it.copy(overlay = event.value) }
            is ShellEvent.Theme -> save { it.copy(theme = event.value.key) }
            is ShellEvent.Animations -> {
                val motionChange = WidgetUpdater.animationsChanging(container)
                save(finished = { WidgetUpdater.animationsSaved(container, motionChange) }) { it.copy(animations = event.enabled) }
            }
            is ShellEvent.PickGroup -> pickGroup(event.id)
            ShellEvent.RetryGroupPick -> mutable.value.pendingGroupId?.let(::pickGroup)
            ShellEvent.RetryProjection -> refresh()
        }
    }

    private fun pickGroup(id: String) {
        val pending = mutable.value.beginGroupPick(id) ?: return
        projectionGate.invalidate()
        mutable.value = pending
        viewModelScope.launch {
                try {
                    withContext(Dispatchers.IO) {
                        if (container.repo.groups().none { it.id == id }) throw IllegalStateException("group no longer available")
                        container.db.runInTransaction {
                            container.repo.saveSettings(container.repo.settings().copy(myGroupId = id))
                        }
                    }
                    container.events.emit(ru.bgtu_voenmeh.zapara.ui.AppEvent.GroupChanged)
                    val name = mutable.value.groups.firstOrNull { it.id == id }?.name ?: id
                    container.toasts.show(container.app.getString(ru.bgtu_voenmeh.zapara.R.string.toast_group, name), ru.bgtu_voenmeh.zapara.ui.components.ToastKind.Ok)
                    mutable.update { it.copy(overlay = ShellOverlay.None, groupId = id, groupName = name,
                        groupPickPending = false, pendingGroupId = null, groupPickError = null) }
                } catch (e: CancellationException) { throw e }
                catch (e: Exception) {
                    android.util.Log.w("ZaparaShell", "PickGroup failed", e)
                    mutable.update { it.copy(groupPickPending = false,
                        groupPickError = container.app.getString(ru.bgtu_voenmeh.zapara.R.string.ux60_group_pick_failed)) }
                }
            }
    }

    fun refresh() {
        viewModelScope.launch { recompute() }
    }

    private fun save(finished: () -> Unit = {}, transform: (ScheduleRepository.SettingsState) -> ScheduleRepository.SettingsState) {
        viewModelScope.launch {
            try {
                withContext(Dispatchers.IO) {
                    container.db.runInTransaction { container.repo.saveSettings(transform(container.repo.settings())) }
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                android.util.Log.w("ZaparaShell", "Preference write failed", e)
                mutable.update { it.copy(error = true) }
            } finally { finished() }
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
