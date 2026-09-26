package ru.bgtu_voenmeh.zapara.data.communities
import org.junit.Test
import org.junit.Assert.*
class GroupAccessPresetsTest {
    @Test fun selected_post_keeps_empty_role_picker_until_first_role_without_changing_wire() {
        val empty=GroupAccessPresets.apply(emptyList(),"post",GroupAccessPresets.Selection(GroupAccessPresets.Mode.Selected))
        assertEquals(GroupAccessPresets.Mode.Headman,GroupAccessPresets.detect(empty,"post").mode)
        val ui=GroupAccessPresets.editorSelection(empty,"post","Selected")
        assertEquals(GroupAccessPresets.Mode.Selected,ui.mode)
        val chosen=GroupAccessPresets.apply(empty,"post",ui.copy(roles=setOf("first")))
        assertEquals(setOf("first"),GroupAccessPresets.detect(chosen,"post").roles)
        val cleared=GroupAccessPresets.apply(chosen,"post",ui)
        assertEquals(GroupAccessPresets.Mode.Selected,GroupAccessPresets.editorSelection(cleared,"post","Selected").mode)
        assertEquals(GroupAccessPresets.Mode.Headman,GroupAccessPresets.editorSelection(cleared,"post",null).mode)
        val advanced=empty+GroupAccessRule("denied","post","deny")
        assertEquals(GroupAccessPresets.Mode.Custom,GroupAccessPresets.editorSelection(advanced,"post","Selected").mode)
    }
    @Test fun presets_replace_only_read_or_post_and_preserve_other_topic_powers() {
        val old=listOf(GroupAccessRule(null,"read","deny"),GroupAccessRule("r1","read","allow"),GroupAccessRule(null,"media","deny"),GroupAccessRule("r2","post","allow"))
        val everyone=GroupAccessPresets.apply(old,"read",GroupAccessPresets.Selection(GroupAccessPresets.Mode.All))
        assertEquals(listOf(GroupAccessRule(null,"read","allow")),everyone.filter { it.power=="read" }); assertTrue(GroupAccessRule(null,"media","deny") in everyone); assertTrue(GroupAccessRule("r2","post","allow") in everyone)
        val inherited=GroupAccessPresets.apply(everyone,"post",GroupAccessPresets.Selection(GroupAccessPresets.Mode.All))
        assertTrue(inherited.none { it.power=="post" }); assertEquals(GroupAccessPresets.Mode.All,GroupAccessPresets.detect(inherited,"post").mode)
    }
    @Test fun selected_roles_and_headman_presets_have_the_agreed_canonical_shapes() {
        val selected=GroupAccessPresets.apply(emptyList(),"read",GroupAccessPresets.Selection(GroupAccessPresets.Mode.Selected,setOf("r2","r1")))
        assertEquals(GroupAccessPresets.Selection(GroupAccessPresets.Mode.Selected,setOf("r1","r2")),GroupAccessPresets.detect(selected,"read"))
        val headman=GroupAccessPresets.apply(selected,"post",GroupAccessPresets.Selection(GroupAccessPresets.Mode.Headman))
        assertEquals(listOf(GroupAccessRule(null,"post","deny")),headman.filter { it.power=="post" }); assertEquals(GroupAccessPresets.Mode.Headman,GroupAccessPresets.detect(headman,"post").mode)
    }
    @Test fun mixed_advanced_rules_are_custom_and_do_not_rewrite_themselves() {
        val mixed=listOf(GroupAccessRule(null,"read","allow"),GroupAccessRule("r","read","deny"))
        assertEquals(GroupAccessPresets.Mode.Custom,GroupAccessPresets.detect(mixed,"read").mode)
        assertEquals(mixed,GroupAccessPresets.apply(mixed,"read",GroupAccessPresets.Selection(GroupAccessPresets.Mode.Custom)))
    }
    @Test fun historical_group_only_rules_are_removed_from_topic_preview_and_save_payloads() {
        val rules=GroupAccessPresets.groupOnly.map { GroupAccessRule(null,it,"deny") }+GroupAccessRule(null,"read","allow")
        assertEquals(listOf(GroupAccessRule(null,"read","allow")),GroupAccessPresets.topicRules(rules))
    }
}
