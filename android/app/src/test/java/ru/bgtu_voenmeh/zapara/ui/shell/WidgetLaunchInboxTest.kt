package ru.bgtu_voenmeh.zapara.ui.shell

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlinx.coroutines.runBlocking
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor

class WidgetLaunchInboxTest {
    private val account = ProfileDescriptor(false, "account-a", "server-a", "profiles/a/zapara.db")

    @Test fun schedule_row_requires_the_captured_profile_and_group() {
        val launch = WidgetLaunchInbox().accept("schedule", "2026-10-01", "account-a", "profiles/a/zapara.db",
            "group-a", "09:30", "математика")!!
        val result = launch.resolve(account, "group-a")!!
        assertEquals("2026-10-01", result.argument)
        assertEquals(ScheduleWidgetTarget("group-a", "09:30", "математика"), result.scheduleTarget)
        assertEquals(WidgetLaunchProblem.OtherGroup, launch.resolve(account, "group-b")!!.problem)
        assertEquals(WidgetLaunchProblem.OtherProfile, launch.resolve(account.copy(userId = "b"), "group-a")!!.problem)
        assertNull(launch.resolve(account, "group-b")!!.scheduleTarget)
    }

    @Test fun schedule_row_restores_before_reading_the_profile_and_group() = runBlocking {
        val launch = WidgetLaunchInbox().accept("schedule", "2026-10-01", "account-a", "profiles/a/zapara.db",
            "group-a", "09:30", "математика")!!
        var profile = ProfileDescriptor.guest()
        var group = "guest-group"
        val result = launch.resolveAfterRestore(restore = { profile = account; group = "group-a" },
            profile = { profile }, group = { group })!!
        assertEquals("2026-10-01", result.argument)
        assertNull(result.problem)
    }

    @Test fun incomplete_or_invalid_schedule_row_opens_generic_schedule() {
        listOf(null, "", "24:10", "9:30", "09:70").forEach { time ->
            val result = WidgetLaunchInbox().accept("schedule", "2026-10-01", "account-a", "profiles/a/zapara.db",
                "group-a", time, "математика")!!.resolve(account, "group-a")!!
            assertNull(result.argument)
            assertEquals(WidgetLaunchProblem.InvalidTarget, result.problem)
        }
    }

    @Test fun homework_scope_requires_matching_profile_and_database() {
        val launch = WidgetLaunchInbox().accept("homework", "41", "account-a", "profiles/a/zapara.db")!!
        assertEquals(WidgetLaunchResolution("41", WidgetLaunchScope.of(account)), launch.resolve(account))
        listOf(account.copy(userId = "account-b"), account.copy(databaseName = "profiles/b/zapara.db"),
            ProfileDescriptor.guest()).forEach { profile ->
            val resolved = launch.resolve(profile)!!
            assertNull(resolved.argument)
            assertNull(resolved.scope)
            assertEquals(WidgetLaunchProblem.OtherProfile, resolved.problem)
        }
        assertNull(launch.resolve(null))
    }

    @Test fun homework_target_without_complete_scope_opens_only_the_list_with_feedback() {
        listOf(null to null, "account-a" to null, null to "profiles/a/zapara.db", "" to "profiles/a/zapara.db").forEach { (profile, db) ->
            val resolved = WidgetLaunchInbox().accept("homework", "41", profile, db)!!.resolve(account)!!
            assertNull(resolved.argument)
            assertEquals(WidgetLaunchProblem.InvalidTarget, resolved.problem)
        }
        val malformed = WidgetLaunchInbox().accept("homework", "-1", "account-a", "profiles/a/zapara.db")!!
        assertEquals(WidgetLaunchProblem.InvalidTarget, malformed.resolve(account)!!.problem)
    }

    @Test fun cold_start_resolves_scope_after_restore_instead_of_against_temporary_guest() = runBlocking {
        val launch = WidgetLaunchInbox().accept("homework", "41", "account-a", "profiles/a/zapara.db")!!
        var current = ProfileDescriptor.guest()
        val order = mutableListOf<String>()
        val resolved = launch.resolveAfterRestore(restore = {
            order += "restore"
            current = account
        }, profile = {
            order += "profile"
            current
        })
        assertEquals(listOf("restore", "profile"), order)
        assertEquals("41", resolved!!.argument)
        assertNull(resolved.problem)
    }

    @Test fun failed_restore_cannot_open_an_account_id_in_the_guest_database() = runBlocking {
        val launch = WidgetLaunchInbox().accept("homework", "41", "account-a", "profiles/a/zapara.db")!!
        val resolved = launch.resolveAfterRestore(restore = {}, profile = { ProfileDescriptor.guest() })!!
        assertNull(resolved.argument)
        assertEquals(WidgetLaunchProblem.OtherProfile, resolved.problem)
    }

    @Test fun legacy_generic_homework_and_other_sections_do_not_require_scope_or_restoration() = runBlocking {
        val generic = WidgetLaunchInbox().accept("homework", null)!!
        assertEquals(WidgetLaunchResolution(null), generic.resolveAfterRestore(
            restore = { error("Generic list must not restore an account") }, profile = { account }))
        assertEquals("2026-09-23", WidgetLaunchInbox().accept("schedule", "2026-09-23")!!.resolve(account)!!.argument)
        assertEquals("493а", WidgetLaunchInbox().accept("maps", "493а")!!.resolve(account)!!.argument)
    }

    @Test fun generic_widget_headers_honor_displayed_profile_without_needing_a_record_id() = runBlocking {
        listOf("homework" to null, "schedule" to "2026-10-02", "maps" to "493").forEach { (section, argument) ->
            val launch = WidgetLaunchInbox().accept(section, argument, account.userId, account.databaseName)!!
            var restored = false
            val result = launch.resolveAfterRestore({ restored = true }, { account })!!
            assertTrue(restored)
            assertEquals(argument, result.argument)
            assertNull(result.problem)
            assertEquals(WidgetLaunchScope.of(account), result.scope)
            assertEquals(WidgetLaunchProblem.OtherProfile, launch.resolve(ProfileDescriptor.guest())!!.problem)
        }
        assertEquals(WidgetLaunchProblem.InvalidTarget,
            WidgetLaunchInbox().accept("homework", null, account.userId, null)!!.resolve(account)!!.problem)
    }

    @Test fun homework_positive_local_id_is_preserved_for_scoped_resolution() {
        val launch = WidgetLaunchInbox().accept("homework", "41")!!
        assertEquals(Section.Homework, launch.section)
        assertEquals("41", launch.argument)
    }

    @Test fun malformed_homework_ids_cannot_become_navigation_arguments() {
        listOf("0", "-1", "+1", " 41 ", "abc", "9223372036854775808", "41?id=42").forEach {
            assertNull(WidgetLaunchInbox().accept("homework", it)!!.argument)
        }
        assertNull(WidgetLaunchInbox().accept("homework", null)!!.argument)
    }

    @Test fun repeated_destination_is_delivered_again() {
        val inbox = WidgetLaunchInbox()
        val first = inbox.accept("schedule", "2026-09-23")!!
        assertEquals(Section.Schedule, first.section)
        assertEquals("2026-09-23", first.argument)
        assertEquals(first, inbox.state.value)

        inbox.consume(first.id)
        assertNull(inbox.state.value)

        val second = inbox.accept("schedule", "2026-09-23")!!
        assertTrue(second.id > first.id)
        assertEquals(second, inbox.state.value)
    }

    @Test fun invalid_schedule_date_opens_schedule_without_date() {
        val launch = WidgetLaunchInbox().accept("schedule", "2026-02-30")!!
        assertEquals(Section.Schedule, launch.section)
        assertNull(launch.argument)
    }

    @Test fun blank_maps_room_opens_map_browsing() {
        val launch = WidgetLaunchInbox().accept("maps", "   ")!!
        assertEquals(Section.Maps, launch.section)
        assertNull(launch.argument)
    }

    @Test fun maps_room_is_trimmed_and_limited_to_one_hundred_characters() {
        val launch = WidgetLaunchInbox().accept("maps", "  ${"A".repeat(101)}  ")!!
        assertEquals("A".repeat(100), launch.argument)
    }

    @Test fun unsupported_section_is_rejected_without_replacing_pending_launch() {
        val inbox = WidgetLaunchInbox()
        val pending = inbox.accept("homework", "ignored")!!
        assertNull(pending.argument)
        assertNull(inbox.accept("community", null))
        assertEquals(pending, inbox.state.value)
    }

    @Test fun consuming_old_id_does_not_clear_newer_launch() {
        val inbox = WidgetLaunchInbox()
        val old = inbox.accept("maps", "101")!!
        val newer = inbox.accept("maps", "102")!!
        inbox.consume(old.id)
        assertEquals(newer, inbox.state.value)
    }
}
