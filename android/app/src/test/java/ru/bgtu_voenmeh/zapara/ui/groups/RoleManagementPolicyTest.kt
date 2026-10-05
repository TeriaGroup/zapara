package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.GroupDesk
import ru.bgtu_voenmeh.zapara.data.communities.GroupGrant
import ru.bgtu_voenmeh.zapara.data.communities.GroupPower
import ru.bgtu_voenmeh.zapara.data.communities.GroupRole
import ru.bgtu_voenmeh.zapara.R

class RoleManagementPolicyTest {
    private val self = GroupPersonUi("me", "Я", "me", "member", true)
    private val member = GroupPersonUi("other", "Друг", "other", "member", false)
    private val curator = GroupPersonUi("curator", "Куратор", "curator", "curator", false)
    private val low = GroupRole("low", "Подгруппа", 1)
    private val high = GroupRole("high", "Помощник", 5)

    @Test fun delegated_manager_cannot_edit_self_equal_roles_or_protected_people() {
        val desk = GroupDesk(false, listOf(low, high), listOf(GroupGrant("high", "me")), emptyList(), listOf("roles", "grants"))
        assertEquals(R.string.group_roles_own, RoleManagementPolicy.roleReason(desk, listOf(self, member), high, "roles"))
        assertNull(RoleManagementPolicy.roleReason(desk, listOf(self, member), low, "roles"))
        assertEquals(R.string.group_roles_self, RoleManagementPolicy.personReason(desk, listOf(self, member), self))
        assertEquals(R.string.group_roles_official_protected, RoleManagementPolicy.personReason(desk, listOf(self, curator), curator))
    }

    @Test fun manager_cannot_delegate_unheld_power_or_assign_to_equal_member() {
        val desk = GroupDesk(false, listOf(low, high), listOf(GroupGrant("high", "me"), GroupGrant("high", "other")), listOf(GroupPower("low", "exclude")), listOf("roles", "grants"))
        assertEquals(R.string.group_roles_unheld, RoleManagementPolicy.roleReason(desk, listOf(self, member), low, "grants"))
        assertEquals(R.string.group_roles_person_equal, RoleManagementPolicy.personReason(desk, listOf(self, member), member))
    }

    @Test fun headman_position_is_bounded_and_delegate_stays_strictly_below_own_level() {
        val headman = GroupDesk(true, listOf(low), emptyList(), emptyList(), listOf("roles"))
        assertTrue(RoleManagementPolicy.positionAllowed(0, headman, listOf(self)))
        assertTrue(RoleManagementPolicy.positionAllowed(10000, headman, listOf(self)))
        assertFalse(RoleManagementPolicy.positionAllowed(10001, headman, listOf(self)))
        assertFalse(RoleManagementPolicy.positionAllowed(-1, headman, listOf(self)))
        val delegate = GroupDesk(false, listOf(low, high), listOf(GroupGrant("high", "me")), emptyList(), listOf("roles"))
        assertTrue(RoleManagementPolicy.positionAllowed(4, delegate, listOf(self)))
        assertFalse(RoleManagementPolicy.positionAllowed(5, delegate, listOf(self)))
    }
}
