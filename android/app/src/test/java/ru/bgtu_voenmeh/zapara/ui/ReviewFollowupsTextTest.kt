package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.InboxSource
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxSubtitleCopy
import ru.bgtu_voenmeh.zapara.ui.inbox.browseInbox
import ru.bgtu_voenmeh.zapara.ui.inbox.inboxSubtitleText
import java.io.File

/** Follow-up ревью: #112 (подпись нажатия карточки) и #117 (поиск по подписи чата, счётчик сроков). */
class ReviewFollowupsTextTest {
    private fun src(path: String) = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/$path").readText()
    private val copy = InboxSubtitleCopy(XmlCopy.get("inbox_subtitle_personal"), XmlCopy.get("inbox_subtitle_group")) {
        XmlCopy.get("inbox_subtitle_personal_in", it)
    }
    private val rows = listOf(
        InboxRow("g", "И831Б", communityId = "c1", source = InboxSource.Group),
        InboxRow("d", "Аня Белова", communityId = "c1", subtitle = "И831Б", source = InboxSource.GroupDirect),
        InboxRow("f", "Миша Орлов", source = InboxSource.Friend))

    @Test fun search_finds_chats_by_their_localized_subtitle_again() {
        val find = { q: String -> browseInbox(rows, q, subtitleOf = { inboxSubtitleText(it, copy) }).map { it.id } }
        assertEquals(listOf("g"), find("учебная группа"))
        assertEquals(listOf("d", "f"), find("личный чат"))
        assertEquals(listOf("d"), find("личный и831б"))
        assertTrue(src("inbox/InboxSection.kt").contains("subtitleOf = { inboxSubtitleText(it, subtitles) }"))
    }

    @Test fun row_and_search_use_the_same_subtitle() {
        assertEquals("Личный чат · И831Б", inboxSubtitleText(rows[1], copy))
        assertEquals("Учебная группа", inboxSubtitleText(rows[0], copy))
        assertTrue(src("inbox/InboxSection.kt").contains("inboxSubtitleText(row, rememberInboxSubtitleCopy())"))
    }

    @Test fun deadline_counter_passes_done_count() {
        val s = src("schedule/ScheduleSection.kt")
        assertEquals(2, Regex("page.deadlines.count \\{ it.done \\}.let \\{ done -> stringResource\\(DeadlineCounter.res\\(done\\), page.deadlines.size, done\\) \\}")
            .findAll(s).count())
        assertEquals(R.string.deadlines_title_count_done, ru.bgtu_voenmeh.zapara.ui.schedule.DeadlineCounter.res(2))
        assertEquals(R.string.deadlines_title_count, ru.bgtu_voenmeh.zapara.ui.schedule.DeadlineCounter.res(0))
        assertEquals("Ближайшие сроки · 5 · выполнено 2", XmlCopy.get("deadlines_title_count_done", 5, 2))
        assertEquals("Ближайшие сроки · 5", XmlCopy.get("deadlines_title_count", 5))
        assertEquals("Ближайшие сроки · 1", XmlCopy.get("deadlines_title_count", 1))
    }

    @Test fun homework_card_names_its_click_action() {
        assertEquals("Открыть действия с заданием", XmlCopy.get("hw_task_open"))
        assertTrue(src("homework/HomeworkSection.kt").contains("onClickLabel = if (selectionMode) null else stringResource(R.string.hw_task_open)"))
        assertTrue(src("theme/Primitives.kt").contains("onClickLabel = onClickLabel,"))
    }
}
