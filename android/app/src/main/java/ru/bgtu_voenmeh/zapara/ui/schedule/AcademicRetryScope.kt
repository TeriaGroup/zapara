package ru.bgtu_voenmeh.zapara.ui.schedule

internal data class AcademicRetryScope(val request: Long, val reloadEpoch: Int,
    val profile: String, val group: String) {
    fun matches(request: Long, reloadEpoch: Int, profile: String, group: String): Boolean =
        this.request == request && this.reloadEpoch == reloadEpoch &&
            this.profile == profile && this.group == group
}
