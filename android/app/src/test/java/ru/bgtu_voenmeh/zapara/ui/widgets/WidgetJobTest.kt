package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork

class WidgetJobTest {
    @Test fun presence_lookup_failure_keeps_failed_clears_pending_and_blocks_new_profile() {
        val preparation = WidgetProfilePreparation()
        val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val placed = WidgetPresence(true, true, false, false, false)
        var scheduleFace = "account-A"
        var homeworkFace = "account-A"
        var failClear = true
        var failLookup = false
        val actions = mutableListOf<String>()
        fun refreshB() {
            val homeworkB = runCatching<String> { error("B homework read failed") }.getOrNull()
            val ready = preparation.prepare(b, { b }, readPresence = {
                if (failLookup) error("AppWidgetManager lookup failed")
                placed
            }, clear = { face ->
                actions += "clear $face"
                if (face == WidgetFace.Homework && failClear) {
                    failClear = false
                    error("Homework clear failed")
                }
                if (face == WidgetFace.Schedule) scheduleFace = ""
                if (face == WidgetFace.Homework) homeworkFace = ""
            })
            if (ready) {
                actions += "publish B"
                scheduleFace = "account-B"
                homeworkB?.let { homeworkFace = it }
            }
        }
        refreshB()
        assertEquals("account-A", homeworkFace)
        assertFalse(actions.contains("publish B"))
        val afterFailedClear = actions.toList()
        failLookup = true
        refreshB()
        assertFalse("Unknown presence must not open the publication barrier", actions.contains("publish B"))
        assertEquals(afterFailedClear, actions)
        assertEquals("", scheduleFace)
        assertEquals("account-A", homeworkFace)
        failLookup = false
        refreshB()
        assertEquals(listOf("clear Schedule", "clear Homework", "clear Homework", "publish B"), actions)
        assertEquals("", homeworkFace)
        assertEquals("account-B", scheduleFace)
    }

    @Test fun failed_profile_clear_blocks_publication_and_retries_when_new_data_is_missing() {
        for (failedFace in listOf("Homework", "Week")) {
            val preparation = PreparationDriver()
            val a = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 1)
            val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
            val placed = WidgetPresence(true, true, true, true, true)
            val faces = linkedMapOf("Schedule" to "", "Homework" to "", "Timer" to "", "Wayfinder" to "", "Week" to "")
            assertTrue(preparation.prepare(a, { a }, placed) { faces[it] = "" })
            faces.keys.forEach { faces[it] = "account-A" }
            val actions = mutableListOf<String>()
            var failOnce = true
            fun refreshB() {
                val homeworkB = runCatching<String> { error("B homework read failed") }.getOrNull()
                val weekB = runCatching<String> { error("B week read failed") }.getOrNull()
                val ready = preparation.prepare(b, { b }, placed) { face ->
                    actions += "clear $face"
                    if (face == failedFace && failOnce) {
                        failOnce = false
                        throw IllegalStateException("launcher clear failed")
                    }
                    faces[face] = ""
                }
                if (ready) {
                    // B's Homework and Week reads failed: only the available face may publish.
                    actions += "publish B"
                    faces["Schedule"] = "account-B"
                    homeworkB?.let { faces["Homework"] = it }
                    weekB?.let { faces["Week"] = it }
                }
            }
            refreshB()
            assertEquals("account-A", faces[failedFace])
            assertFalse(actions.contains("publish B"))
            assertFalse(faces.values.contains("account-B"))
            val firstActions = actions.size
            refreshB()
            assertEquals(listOf("clear $failedFace", "publish B"), actions.drop(firstActions))
            assertEquals("", faces["Homework"])
            assertEquals("", faces["Week"])
            assertEquals("account-B", faces["Schedule"])
        }
    }

    @Test fun profile_change_during_clear_retry_rejects_b_and_prepares_c_from_scratch() {
        val preparation = PreparationDriver()
        val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val c = WidgetJobIdentity.of(ProfileDescriptor.guest(), 3)
        var current = b
        val placed = WidgetPresence(false, true, false, false, true)
        val actions = mutableListOf<String>()
        assertFalse(preparation.prepare(b, { current }, placed) { face ->
            actions += "B $face"
            if (face == "Week") throw IllegalStateException("launcher clear failed")
        })
        assertFalse(preparation.prepare(b, { current }, placed) { face ->
            actions += "retry B $face"
            current = c
        })
        assertFalse(preparation.prepare(b, { current }, placed) { error("stale B must not clear C") })
        assertTrue(preparation.prepare(c, { current }, placed) { face -> actions += "C $face" })
        assertEquals(listOf("B Homework", "B Week", "retry B Week", "C Homework", "C Week"), actions)
    }

    @Test fun removing_the_failed_widget_type_releases_the_profile_clear_barrier() {
        val preparation = PreparationDriver()
        val identity = WidgetJobIdentity.of(ProfileDescriptor.guest(), 1)
        assertFalse(preparation.prepare(identity, { identity }, WidgetPresence(false, true, false, false, true)) {
            if (it == "Week") throw IllegalStateException("launcher clear failed")
        })
        assertTrue(preparation.prepare(identity, { identity }, WidgetPresence(false, true, false, false, false)) {
            error("already cleared Homework must not repeat")
        })
    }

    private class PreparationDriver {
        private val value = WidgetProfilePreparation()
        fun prepare(identity: WidgetJobIdentity, current: () -> WidgetJobIdentity, placed: WidgetPresence, clear: (String) -> Unit): Boolean {
            return value.prepare(identity, current, { placed }, clear = { clear(it.name) })
        }
    }

    @Test fun advance_is_only_needed_for_timeline_faces() {
        assertFalse(WidgetPresence(false, false, false, false, false).needsAdvance)
        assertFalse(WidgetPresence(false, true, false, false, true).needsAdvance)
        assertTrue(WidgetPresence(true, false, false, false, false).needsAdvance)
        assertTrue(WidgetPresence(false, false, true, false, false).needsAdvance)
        assertTrue(WidgetPresence(false, false, false, true, false).needsAdvance)
    }

    private val key = "A".repeat(64)
    private val userA = "11111111-1111-4111-8111-111111111111"
    private val userB = "22222222-2222-4222-8222-222222222222"

    @Test fun identity_uses_guest_db_and_account_path() {
        val guest = WidgetJobIdentity.of(ProfileDescriptor.guest(), 0)
        assertEquals("guest", guest.profileId)
        assertEquals("zapara.db", guest.databaseName)
        assertEquals(0L, guest.generation)
        assertTrue(guest.isGuest)
        val account = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 4)
        assertEquals(userA, account.profileId)
        assertEquals("profiles/$key/$userA/zapara.db", account.databaseName)
        assertEquals(4L, account.generation)
        assertFalse(account.isGuest)
    }

    @Test fun accept_requires_same_profile_and_generation() {
        val a1 = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 1)
        val a2 = a1.copy(generation = 2)
        val b = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val guest = WidgetJobIdentity.of(ProfileDescriptor.guest(), 2)
        assertTrue(WidgetJobs.accept(a1, a1))
        assertFalse(WidgetJobs.accept(a1, a2))
        assertFalse(WidgetJobs.accept(a1, b))
        assertFalse(WidgetJobs.accept(a1, guest))
        assertFalse(WidgetJobs.accept(b, guest))
    }

    @Test fun stale_profile_work_ticket_rejects_even_if_identity_matches() {
        val work = ProfileWork()
        val ticket = work.enter()
        val job = WidgetJobIdentity.of(ProfileDescriptor.guest(), 0)
        assertTrue(WidgetJobs.canApply(ticket, job, job))
        work.stopAccepting()
        assertFalse(ticket.isCurrent)
        assertFalse(WidgetJobs.canApply(ticket, job, job))
        ticket.close()
    }

    @Test fun logout_and_switch_drop_account_a_callback() {
        val accountA = WidgetJobIdentity.of(ProfileDescriptor.account(key, userA), 1)
        val accountB = WidgetJobIdentity.of(ProfileDescriptor.account(key, userB), 2)
        val guest = WidgetJobIdentity.of(ProfileDescriptor.guest(), 3)
        val work = ProfileWork()
        val ticket = work.enter()
        assertFalse(WidgetJobs.canApply(ticket, accountA, accountB))
        assertFalse(WidgetJobs.canApply(ticket, accountA, guest))
        assertTrue(WidgetJobs.canApply(ticket, guest, guest))
        ticket.close()
    }

    @Test fun unused_rows_bind_blank_text_so_reapply_cannot_keep_account_a() {
        val gone = WidgetRowBind.hidden()
        assertFalse(gone.visible)
        assertEquals("", gone.primary)
        assertEquals("", gone.secondary)
        val shown = WidgetRowBind.visible("Матан", "secret-account-A-homework")
        assertTrue(shown.visible)
        assertEquals("secret-account-A-homework", shown.secondary)
        assertEquals("", WidgetRowBind.of(null as ScheduleWidgetRow?).primary)
        assertEquals("", WidgetRowBind.of(null as HomeworkWidgetRow?).primary)
    }

    @Test fun guest_first_widget_process_restores_vault_account_does_not() = kotlinx.coroutines.runBlocking {
        var restored = 0
        WidgetVaultRestore.beforePush(isGuest = true) { restored++ }
        assertEquals(1, restored)
        WidgetVaultRestore.beforePush(isGuest = false) { restored++ }
        assertEquals(1, restored)
    }
}
