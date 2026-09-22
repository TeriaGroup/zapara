package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import java.net.URI

class ApiRefreshCoordinator(
    private val store: TimetableStore,
    private val work: ProfileWork,
    baseUrl: String?,
    transport: HttpExchange,
    private val saveSettings: (ScheduleRepository.SettingsState) -> Unit = store::saveSettings
) {
    private val client: TimetableApiClient?
    private val sourceBase: String
    private val refreshLock = Mutex()
    @Volatile private var stopped = false
    var configured: Boolean = baseUrl != null
        private set
    var configurationError: String? = null
        private set
    var lastError: String? = null
        private set
    var lastFailure: TimetableApiFailure? = null
        private set

    init {
        if (baseUrl == null) {
            client = null
            sourceBase = ""
            store.useApiCatalog = false
        } else {
            store.useApiCatalog = true
            val parsed = try {
                val uri = URI(baseUrl)
                val created = TimetableApiClient(transport, uri)
                created to TimetableApiClient.validateBaseUri(uri).toString()
            } catch (_: Exception) {
                configurationError = "Некорректный адрес API расписания. Проверьте локальную конфигурацию."
                null
            }
            client = parsed?.first
            sourceBase = parsed?.second.orEmpty()
        }
    }

    suspend fun refresh(neededOnly: Boolean = false): Boolean {
        val ticket = work.enter()
        try {
            if (!ticket.isCurrent) return false
            if (!configured || stopped) return false
            if (store.settings().useUniversityXml) return false
            val http = client ?: run {
                lastError = configurationError
                return false
            }
            return refreshLock.withLock {
                ticket.throwIfStale()
                val cache = TimetableApiCache(store)
                val before = store.settings()
                val selected = before.myGroupId
                val selectedName = store.storedGroups().firstOrNull { it.id == selected }?.name
                val friendNames = store.friends().filter { it.enabled }.map { it.groupName }
                var ids = linkedSetOf<String>()
                if (selected != null) ids.add(selected)
                val known = store.groups().associateBy { it.name }
                val requirements = mutableListOf<TimetableGroupRequest>()
                if (selected != null) requirements.add(TimetableGroupRequest(selected, selectedName))
                for (name in friendNames) {
                    val id = known[name]?.id
                    if (!id.isNullOrEmpty()) ids.add(id)
                    requirements.add(TimetableGroupRequest(id, name))
                }
                if (neededOnly && requirements.all { it.id != null } && cache.read("")?.sourceBase == sourceBase && ids.isNotEmpty() &&
                    ids.all { id ->
                        val m = cache.read(id)
                        m?.source == "api" && m.sourceBase == sourceBase &&
                            m.meta?.snapshotId == cache.read("")?.meta?.snapshotId
                    }
                ) return@withLock false
                val snapshot = try {
                    http.fetchResolved(requirements)
                } catch (e: TimetableApiException) {
                    ticket.throwIfStale()
                    lastFailure = e.failure
                    throw e
                }
                ticket.throwIfStale()
                if (stopped) return@withLock false
                if (store.settings().useUniversityXml || store.settings().myGroupId != selected || store.friends().filter { it.enabled }.map { it.groupName } != friendNames)
                    return@withLock false
                val resolvedSelected = selected?.let { TimetableGroupRequest(it, selectedName).resolve(snapshot.groups).id }
                try { cache.apply(snapshot, sourceBase, resolvedSelected, saveSettings, before) }
                catch (_: StaleTimetableSelection) { return@withLock false }
                lastError = null
                lastFailure = null
                true
            }
        } catch (e: CancellationException) {
            if (stopped || !ticket.isCurrent) return false
            throw e
        } catch (_: Exception) {
            if (!ticket.isCurrent) return false
            lastError = "Не удалось обновить расписание API. Локальные данные сохранены."
            return false
        } finally {
            ticket.close()
        }
    }

    fun stop() {
        stopped = true
        work.stopAccepting()
    }
}
