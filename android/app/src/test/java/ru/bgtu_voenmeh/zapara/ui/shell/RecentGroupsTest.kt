package ru.bgtu_voenmeh.zapara.ui.shell

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** #123 follow-up: «Недавние» — у каждого профиля свои, и группа попадает туда только после успешного выбора. */
class RecentGroupsTest {
    private fun store(): Pair<RecentGroups, MutableMap<String, String>> {
        val map = mutableMapOf<String, String>()
        return RecentGroups({ map[it] }, { k, v -> map[k] = v }) to map
    }

    @Test fun recent_groups_are_kept_per_profile() {
        val (recent, map) = store()
        recent.push("zapara_guest", "1")
        recent.push("zapara_guest", "2")
        recent.push("zapara_user_42", "9")
        assertEquals(listOf("2", "1"), recent.ids("zapara_guest"))
        assertEquals(listOf("9"), recent.ids("zapara_user_42"))
        assertEquals(emptyList<String>(), recent.ids("zapara_user_7"))
        assertEquals(setOf("recent:zapara_guest", "recent:zapara_user_42"), map.keys)
        recent.push("zapara_guest", "3"); recent.push("zapara_guest", "4")
        assertEquals("не больше трёх", listOf("4", "3", "2"), recent.ids("zapara_guest"))
    }

    @Test fun only_a_successful_pick_is_recorded() {
        val vm = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/shell/ShellViewModel.kt").readText()
        val pick = vm.substring(vm.indexOf("private fun pickGroup("), vm.indexOf("fun refresh()"))
        val saved = pick.indexOf("container.repo.saveSettings(")
        val pushed = pick.indexOf("recentGroups.push(profile, id)")
        val failed = pick.indexOf("catch (e: Exception)")
        assertTrue("push после сохранения", saved in 0 until pushed)
        assertTrue("push не в ветке ошибки", pushed < failed)
        assertTrue(pick.contains("val profile = container.profile.databaseName"))
        val app = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/shell/ZaparaApp.kt").readText()
        assertFalse("экран не пишет «Недавние» до результата", app.contains("recentGroups.push("))
        assertTrue(app.contains("onPick = { id -> shellVm.onEvent(ShellEvent.PickGroup(id)) }"))
        assertTrue(app.contains("shellVm.recentGroupIds()"))
    }
}
