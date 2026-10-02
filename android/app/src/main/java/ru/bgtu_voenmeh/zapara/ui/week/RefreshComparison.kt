package ru.bgtu_voenmeh.zapara.ui.week

internal fun refreshComparison(before: WeekUiState, after: WeekUiState): List<WeekChange>? {
    if (!before.loaded || !after.loaded || before.noSavedSchedule || after.noSavedSchedule || after.error != null ||
        before.fetchedAt == null || after.fetchedAt == null ||
        (before.fetchedAt == after.fetchedAt && before.sourceContext == after.sourceContext) ||
        before.profileName != after.profileName || before.groupId.isBlank() || before.groupId != after.groupId ||
        before.sourceBaseContext != after.sourceBaseContext || before.days.size != 7 ||
        before.days.map { it.date } != after.days.map { it.date }) return null
    return compareWeeks(before.days, after.days).filter { it.removed.isNotEmpty() || it.added.isNotEmpty() }
}
