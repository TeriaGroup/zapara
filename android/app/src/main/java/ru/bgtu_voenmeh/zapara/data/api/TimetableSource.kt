package ru.bgtu_voenmeh.zapara.data.api

import ru.bgtu_voenmeh.zapara.data.ScheduleRepository

class TimetableSource(
    private val api: ApiRefreshCoordinator,
    private val store: TimetableStore,
    private val xmlRefresh: suspend () -> Unit,
    private val bundled: suspend () -> Boolean = { false }
) {
    suspend fun ensure() {
        if (store.groups().isNotEmpty()) return
        if (ScheduleRepository.networkEnabled && bundled()) return
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

    companion object {
        const val XML_REFUSED = "Для API используется типизированная загрузка расписания."

        fun guardXmlRefresh(store: TimetableStore, settings: ScheduleRepository.SettingsState) {
            if (store.useApiCatalog && !settings.useUniversityXml) {
                throw IllegalStateException(XML_REFUSED)
            }
        }
    }
}
