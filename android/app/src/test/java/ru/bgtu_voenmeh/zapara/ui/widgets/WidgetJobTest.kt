package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork

class WidgetJobTest {
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
