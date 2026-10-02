package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId

class TimetableSource(
    private val api: ApiRefreshCoordinator,
    private val store: TimetableStore,
    private val xmlRefresh: suspend () -> Unit,
    private val bundled: suspend () -> Boolean = { false }
) {
    private val automaticRefreshLock = Mutex()
    private var lastAutomaticAttempt: Long? = null

    suspend fun ensure() {
        if (store.groups().isNotEmpty()) return
        if (ScheduleRepository.networkEnabled) {
            try {
                if (bundled()) return
            } catch (e: CancellationException) {
                throw e
            } catch (_: Throwable) {
            }
        }
        if (!ScheduleRepository.networkEnabled) throw IllegalStateException("empty db and network disabled (tests)")
        if (!pull() && usesJson()) {
            throw IllegalStateException(api.lastError ?: XML_REFUSED)
        }
    }

    suspend fun pull(): Boolean {
        if (usesJson()) return api.refresh()
        xmlRefresh()
        return true
    }

    private fun usesJson(): Boolean {
        val xml = store.settings().useUniversityXml
        return api.configured && !xml
    }

    /** A warm cache stays readable offline; a failed check cannot block the screen or erase it. */
    suspend fun refreshIfStale(): Boolean {
        if (!ScheduleRepository.networkEnabled || !automaticRefreshLock.tryLock()) return false
        try {
            val selected = store.settings().myGroupId?.takeIf(String::isNotBlank) ?: return false
            val metadata = if (usesJson()) store.readMetadata(selected) else null
            val stamp = metadata?.fetchedAt ?: store.settings().lastFetchedAt
            val fetched = stamp?.let { value ->
                runCatching { OffsetDateTime.parse(value).toInstant() }.getOrNull()
                    ?: runCatching { LocalDateTime.parse(value).atZone(ZoneId.systemDefault()).toInstant() }.getOrNull()
            }
            val stale = metadata?.meta?.stale == true || fetched == null ||
                Duration.between(fetched, Instant.now()) >= Duration.ofHours(24)
            if (!stale) return false
            val attempt = System.nanoTime()
            if (lastAutomaticAttempt?.let { attempt - it < Duration.ofMinutes(15).toNanos() } == true) return false
            lastAutomaticAttempt = attempt
            return try {
                pull()
            } catch (e: CancellationException) {
                throw e
            } catch (_: Exception) {
                // Manual refresh still reports the failure; automatic checks preserve offline access.
                false
            }
        } finally {
            automaticRefreshLock.unlock()
        }
    }

    companion object {
        const val XML_REFUSED = "Для API используется типизированная загрузка расписания."

        fun guardXmlRefresh(store: TimetableStore, settings: ScheduleRepository.SettingsState) {
            if (store.useApiCatalog && !settings.useUniversityXml) {
                throw IllegalStateException(XML_REFUSED)
            }
        }
    }
}
