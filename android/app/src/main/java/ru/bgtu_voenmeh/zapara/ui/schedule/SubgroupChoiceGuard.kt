package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.data.Subgroups

data class SubgroupUndoUi(
    val profile: String,
    val groupId: String,
    val streamId: String,
    val before: String?,
    val after: String?
) {
    fun allows(profile: String, groupId: String, current: String?, streams: List<Subgroups.Stream>): Boolean =
        this.profile == profile && this.groupId == groupId && current == after &&
            streams.any { stream -> stream.id == streamId &&
                (before == null || stream.options.any { it.id == before }) }
}

internal fun subgroupOptionExists(streams: List<Subgroups.Stream>, streamId: String, optionId: String): Boolean =
    streams.any { stream -> stream.id == streamId && stream.options.any { it.id == optionId } }
