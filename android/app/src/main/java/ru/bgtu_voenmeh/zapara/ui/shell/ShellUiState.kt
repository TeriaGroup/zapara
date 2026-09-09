package ru.bgtu_voenmeh.zapara.ui.shell

import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice

enum class ShellOverlay { None, Sections, GroupPicker }

data class ShellUiState(
    val loaded: Boolean = false,
    val groupId: String? = null,
    val groupName: String? = null,
    val groups: List<GroupInfo> = emptyList(),
    val odd: Boolean = true,
    val stale: Boolean = false,
    val homeworkBadge: Int = 0,
    val theme: ThemeChoice = ThemeChoice.System,
    val animations: Boolean = true,
    val overlay: ShellOverlay = ShellOverlay.None,
    val error: Boolean = false
) { val hasGroup get() = !groupId.isNullOrEmpty() }

sealed interface ShellEvent {
    data class Overlay(val value: ShellOverlay) : ShellEvent
    data class Theme(val value: ThemeChoice) : ShellEvent
    data class Animations(val enabled: Boolean) : ShellEvent
    data class PickGroup(val id: String) : ShellEvent
}
