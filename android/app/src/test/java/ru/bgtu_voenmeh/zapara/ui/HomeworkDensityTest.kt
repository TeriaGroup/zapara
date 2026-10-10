package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkCompletionFilter
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkDensity
import java.io.File

/** #101 (AN-04, AN-10, AN-20, AN-26): плотный экран «Домашка». */
class HomeworkDensityTest {
    private val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/homework/HomeworkSection.kt").readText()
    private val sheet = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/homework/HomeworkTaskSheet.kt").readText()
    private fun strings(name: String) = File("src/main/res/values/$name").readText()

    @Test fun full_error_only_when_there_is_nothing_to_show() {
        assertTrue(HomeworkDensity.fullError("Не удалось открыть базу", 0, 0))
        assertFalse("сохранённые задания остаются видны", HomeworkDensity.fullError("ошибка", 3, 0))
        assertFalse(HomeworkDensity.fullError(null, 0, 0))
    }

    @Test fun empty_list_shows_only_the_empty_state() {
        assertTrue(HomeworkDensity.nothingYet(null, 0, 0, false))
        assertFalse(HomeworkDensity.nothingYet(null, 1, 0, false))
        assertFalse("общие ещё грузятся", HomeworkDensity.nothingYet(null, 0, 0, true))
        assertFalse("ошибка — другой экран", HomeworkDensity.nothingYet("x", 0, 0, false))
        assertTrue(section.contains("if (!nothingYet) item(\"browse\")") && section.contains("if (!nothingYet) item(\"summary\")"))
    }

    @Test fun found_count_only_when_the_list_is_narrowed() {
        assertFalse(HomeworkDensity.filtered("", HomeworkCompletionFilter.Active, false, null))
        assertTrue(HomeworkDensity.filtered("физ", HomeworkCompletionFilter.Active, false, null))
        assertTrue(HomeworkDensity.filtered("", HomeworkCompletionFilter.All, false, null))
        assertTrue(HomeworkDensity.filtered("", HomeworkCompletionFilter.Active, true, null))
        assertTrue(HomeworkDensity.filtered("", HomeworkCompletionFilter.Active, false, "Физика"))
    }

    @Test fun utilities_live_in_the_overflow_menu_not_above_the_list() {
        val menu = section.substringAfter("\"Homework.More\")").substringBefore("Box(Modifier.weight(1f))")
        listOf("Homework.PlanTools", "Homework.SelectMode", "Homework.ExpandGroups", "Homework.CollapseGroups").forEach {
            assertTrue("$it в меню «⋯»", menu.contains(it))
        }
        assertFalse("нет ZDisclosureButton «Инструменты домашки»", section.contains("ZDisclosureButton(stringResource(if (planToolsOpen)"))
        assertTrue("«Ещё фильтры» — чип", section.contains("onClick = { advancedOpen = !advancedOpen }, tag = \"Homework.MoreFilters\""))
    }

    @Test fun one_counter_line() {
        assertTrue(section.contains("\"Homework.Counts\""))
        assertFalse(section.contains("\"Homework.OpenCount\""))
        assertTrue(strings("strings_homework_density.xml").contains("Открыто %1\$d · Выполнено %2\$d"))
    }

    @Test fun card_is_subject_text_due_and_switch_the_rest_is_in_the_task_sheet() {
        val card = section.substringAfter("tag = \"Homework.Row.\${item.id}\"").substringBefore("taskSheet?.let")
        listOf("Homework.Edit.", "Homework.Delete.", "Homework.NextLesson.", "Homework.Clone.").forEach {
            assertFalse("$it не на карточке", card.contains(it)); assertTrue("$it в листе", sheet.contains(it))
        }
        assertTrue(card.contains("\"Homework.Done.\${item.id}\""))
        assertTrue(section.contains("else taskSheet = item.id"))
    }

    @Test fun error_and_empty_states_have_one_primary_action() {
        assertTrue(section.contains("fullError -> EmptyState(R.drawable.ic_alert, stringResource(R.string.hw_load_error_title)"))
        assertTrue(strings("strings_homework_density.xml").contains("Не удалось загрузить домашку"))
        assertTrue(section.contains("stringResource(R.string.hw_empty_hint), stringResource(R.string.hw_add_task)"))
        assertFalse("«+» не висит в подсказке", strings("strings.xml").substringAfter("name=\"hw_empty_hint\">").substringBefore("<").contains("＋"))
    }

    @Test fun section_headers_are_48dp_and_switch_label_uses_glossary() {
        assertTrue(section.contains("heightIn(min = Zapara.space.minTouch) // #101 / AN-26"))
        assertTrue(strings("strings.xml").contains("<string name=\"hw_completion_label\">Выполнено: %1\$s, %2\$s</string>"))
    }
}
