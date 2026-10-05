package ru.bgtu_voenmeh.zapara.ui.shell

/** One pending group write owns the picker until it settles. */
internal fun ShellUiState.beginGroupPick(id: String): ShellUiState? =
    if (groupPickPending || groups.none { it.id == id }) null
    else copy(groupPickPending = true, pendingGroupId = id, groupPickError = null)
