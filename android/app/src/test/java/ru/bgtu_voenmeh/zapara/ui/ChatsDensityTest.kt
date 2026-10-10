package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.chat.chatListTime
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxDensity
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic
import java.io.File
import java.time.LocalDate
import java.time.ZoneId
import java.time.ZonedDateTime

/** #108: список чатов и беседы (AN-13, AN-17). */
class ChatsDensityTest {
    private val ui = File("src/main/java/ru/bgtu_voenmeh/zapara/ui")
    private fun src(path: String) = File(ui, path).readText()

    @Test fun filters_only_from_ten_chats_or_while_one_is_on() {
        assertFalse(InboxDensity.showFilters(chats = 4, activeFilters = 0))
        assertFalse(InboxDensity.showFilters(chats = 9, activeFilters = 0))
        assertTrue(InboxDensity.showFilters(chats = 10, activeFilters = 0))
        assertTrue("включённый фильтр можно снять", InboxDensity.showFilters(chats = 4, activeFilters = 1))
    }

    @Test fun no_stats_lines_one_found_line_only_while_narrowed() {
        val s = src("inbox/InboxSection.kt")
        assertFalse(s.contains("R.string.inbox_unread_total"))
        assertFalse(s.contains("R.string.ux30_inbox_unread_chats"))
        assertTrue(s.contains("if (filtered) Text(stringResource(R.string.inbox_results"))
    }

    @Test fun today_shows_time_other_days_a_date() {
        val zone = ZoneId.of("Europe/Moscow")
        val today = LocalDate.of(2026, 10, 8)
        assertEquals("10:43", chatListTime(ZonedDateTime.of(2026, 10, 8, 10, 43, 0, 0, zone).toInstant(), today, zone, "вчера"))
        assertEquals("06.10", chatListTime(ZonedDateTime.of(2026, 10, 6, 9, 0, 0, 0, zone).toInstant(), today, zone, "вчера"))
    }

    @Test fun group_without_photo_gets_a_glyph_not_two_letters() {
        val s = src("chat/ChatAvatar.kt")
        assertTrue(s.contains("target?.kind == AvatarKind.Group) Icon(painterResource(R.drawable.ic_users)"))
    }

    @Test fun one_label_for_chats() {
        assertEquals("Чаты", XmlCopy.get("nav_chat"))
        assertEquals(XmlCopy.get("chat_header_chats"), XmlCopy.get("nav_chat"))
    }

    @Test fun bottom_bar_is_hidden_in_an_open_conversation() {
        assertFalse(ShellLogic.showBottomBar(Section.Chat, conversationOpen = true))
        assertFalse(ShellLogic.showBottomBar(Section.Group, conversationOpen = true))
        assertTrue(ShellLogic.showBottomBar(Section.Chat, conversationOpen = false))
        assertTrue(ShellLogic.showBottomBar(Section.Schedule, conversationOpen = true))
        assertTrue(src("inbox/InboxSection.kt").contains("ReportConversationOpen(state.active != null)"))
        assertTrue(src("groups/GroupSection.kt").contains("ReportConversationOpen(conversation)"))
        assertTrue(src("shell/ZaparaApp.kt").contains("if (ShellLogic.showBottomBar(current, conversation.open)) ZBottomBar("))
    }

    @Test fun group_conversation_lights_the_chats_tab() {
        assertTrue(src("shell/ZaparaApp.kt").contains(
            "val barCurrent = if (current == Section.Group && !entry?.arguments?.getString(\"communityId\").isNullOrBlank()) Section.Chat else current"))
    }

    @Test fun own_personal_bubble_hugs_the_right_edge() {
        val s = src("inbox/InboxSection.kt")
        assertTrue(s.contains("horizontalArrangement = if (mine) Arrangement.End else Arrangement.Start"))
        val time = s.substring(s.indexOf("R.string.face_read else R.string.face_sent"), s.indexOf("Inbox.MessageTime."))
        assertFalse("weight(1f) растягивал пузырь на всю ширину", time.contains("weight(1f)"))
    }

    @Test fun a_replaced_screen_cannot_reset_the_flag_of_the_screen_that_replaced_it() {
        val state = ru.bgtu_voenmeh.zapara.ui.shell.ConversationOpenState()
        val first = Any(); val second = Any()
        assertFalse(state.open)
        state.report(first, true)
        // NavHost: новая беседа сообщает о себе раньше, чем старая успевает уйти из композиции
        state.report(second, true)
        state.report(first, false)
        assertTrue("dispose старого экрана не сбрасывает флаг нового", state.open)
        state.report(second, true)  // повтор — без дублей
        state.report(second, false)
        assertFalse(state.open)
        state.report(first, false)  // повторный dispose — не ломает
        assertFalse(state.open)
        val chrome = src("shell/ConversationChrome.kt")
        assertTrue(chrome.contains("val owner = remember { Any() }"))
        assertTrue(chrome.contains("onDispose { state.report(owner, false) }"))
    }
}
