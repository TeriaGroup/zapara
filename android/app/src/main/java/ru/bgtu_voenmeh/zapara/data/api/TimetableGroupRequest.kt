package ru.bgtu_voenmeh.zapara.data.api

/** A stored local id and its known group name, resolved against one pinned catalog generation. */
data class TimetableGroupRequest(val id: String?, val name: String?) {
    fun resolve(groups: List<TimetableApiGroup>): TimetableApiGroup {
        val direct = id?.let { wanted -> groups.firstOrNull { it.id == wanted } }
        val known = name?.trim()?.takeIf { it.isNotEmpty() }
        if (direct != null && (known == null || direct.name.equals(known, ignoreCase = true))) return direct
        val wanted = known ?: id
        val matches = groups.filter { it.name.equals(wanted, ignoreCase = true) }.take(2)
        if (matches.size != 1) throw TimetableApiException(TimetableApiFailure.UnknownRequiredGroup)
        return matches.single()
    }
}
