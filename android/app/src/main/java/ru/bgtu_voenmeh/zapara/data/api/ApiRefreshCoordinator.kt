package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import java.net.URI

class ApiRefreshCoordinator(
    private val store: TimetableStore,
    private val work: ProfileWork,
    baseUrl: String?,
    transport: HttpExchange
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
                val selected = store.settings().myGroupId
                val friendNames = store.friends().filter { it.enabled }.map { it.groupName }
                var ids = linkedSetOf<String>()
                if (selected != null) ids.add(selected)
                val known = store.groups().associateBy { it.name }
                var unresolved = false
                for (name in friendNames) {
                    val id = known[name]?.id
                    if (id.isNullOrEmpty()) unresolved = true else ids.add(id)
                }
                if (neededOnly && cache.read("")?.sourceBase == sourceBase && ids.isNotEmpty() &&
                    ids.all { id ->
                        val m = cache.read(id)
                        m?.source == "api" && m.sourceBase == sourceBase &&
                            m.meta?.snapshotId == cache.read("")?.meta?.snapshotId
                    }
                ) return@withLock false
                val snapshot = try {
                    val fetchIds = if (unresolved) emptyList() else ids.toList()
                    var result = http.fetch(fetchIds)
                    if (unresolved && selected != null) {
                        val byName = result.groups.associateBy { it.name }
                        val resolved = linkedSetOf(selected)
                        for (name in friendNames) {
                            val g = byName[name] ?: throw TimetableApiException(TimetableApiFailure.UnknownRequiredGroup)
                            resolved.add(g.id)
                        }
                        result = http.fetch(resolved.toList())
                    }
                    result
                } catch (e: TimetableApiException) {
                    ticket.throwIfStale()
                    lastFailure = e.failure
                    throw e
                }
                ticket.throwIfStale()
                if (stopped) return@withLock false
                cache.apply(snapshot, sourceBase)
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
