package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import java.io.File

class ShellContractTest {
    private val root = File("src/main/java/ru/bgtu_voenmeh/zapara")
    @Test fun application_owns_guest_composition() {
        assertTrue(File(root, "ZaparaApplication.kt").exists())
        assertTrue(File("src/main/AndroidManifest.xml").readText().contains(".ZaparaApplication"))
    }
    @Test fun main_uses_new_shell() {
        assertTrue(File(root, "MainActivity.kt").readText().contains("ui.shell.ZaparaApp"))
    }
    @Test fun navigation_is_pinned() {
        assertTrue(File("build.gradle.kts").readText().contains("navigation-compose:2.7.7"))
    }
    @Test fun first_launch_does_not_select_a_demo_group() {
        val source = File(root, "ui/schedule/ScheduleViewModel.kt").readText()
        assertFalse(source.contains("groups.firstOrNull { it.id == \"3313\" }"))
        assertFalse(source.contains("gid.ifEmpty { groups.firstOrNull()"))
    }
    @Test fun community_route_is_composed_from_app_container() {
        val app = File(root, "ui/shell/ZaparaApp.kt").readText()
        assertTrue(app.contains("Section.Community.route"))
        assertTrue(app.contains("CommunitiesViewModel.factory(container)"))
        assertTrue(app.contains("CommunitiesSection("))
        assertTrue(File(root, "ui/communities/CommunitiesViewModel.kt").isFile)
    }
    @Test fun room_outbox_is_wired_for_mutations_and_non_guest_push() {
        val source = File(root, "ZaparaApplication.kt").readText()
        assertTrue(source.contains("RoomSyncOutbox.from"))
        assertTrue(source.contains("enabled = !profile.isGuest") || source.contains("enabled=!profile.isGuest"))
        assertTrue(source.contains("HomeworkService("))
        assertTrue(source.contains("OverrideService("))
        assertTrue(source.contains("outbox"))
        assertTrue(source.contains("PrivateSyncCoordinator"))
        assertTrue(source.contains("attachPrivateSync") || source.contains(".attach("))
        assertTrue(File(root, "data/sync/PrivateSyncCoordinator.kt").isFile)
    }
}
