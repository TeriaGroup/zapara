package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import org.w3c.dom.Element
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

/** #106 (AN-07, AN-21, AN-28): склонения по числу и строки из кода в ресурсах. */
class PluralsTest {
    private val res = File("src/main/res/values")
    private val plurals: Map<String, Map<String, String>> = res.listFiles()!!.filter { it.extension == "xml" }.flatMap { file ->
        val doc = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val list = doc.getElementsByTagName("plurals")
        (0 until list.length).map { i ->
            val el = list.item(i) as Element
            val items = el.getElementsByTagName("item")
            el.getAttribute("name") to (0 until items.length).associate { j ->
                val item = items.item(j) as Element
                item.getAttribute("quantity") to item.textContent
            }
        }
    }.toMap()

    /** Правило CLDR для русского (как у Android для целых чисел). */
    private fun quantity(n: Int): String = when {
        n % 10 == 1 && n % 100 != 11 -> "one"
        n % 10 in 2..4 && n % 100 !in 12..14 -> "few"
        else -> "many"
    }
    private fun say(name: String, n: Int) = plurals.getValue(name).getValue(quantity(n)).replace("%d", n.toString())

    @Test fun every_new_plural_has_all_russian_forms() {
        listOf("channel_ballot_count", "ux300_android_password_remaining", "ux300_android_password_over",
            "ux300_android_rename_more", "ux30_study_show_more_homework", "deadlines_title_count").forEach { name ->
            assertEquals(name, setOf("one", "few", "many", "other"), plurals.getValue(name).keys)
        }
    }

    @Test fun forms_for_1_2_5_11_21() {
        assertEquals("Голосования · 1 активное", say("channel_ballot_count", 1))
        assertEquals("Голосования · 2 активных", say("channel_ballot_count", 2))
        assertEquals("До минимальной длины остался 1 символ", say("ux300_android_password_remaining", 1))
        assertEquals("До минимальной длины осталось 2 символа", say("ux300_android_password_remaining", 2))
        assertEquals("До минимальной длины осталось 5 символов", say("ux300_android_password_remaining", 5))
        assertEquals("До минимальной длины осталось 11 символов", say("ux300_android_password_remaining", 11))
        assertEquals("Длина превышена на 21 символ", say("ux300_android_password_over", 21))
        assertEquals("И ещё 1 пара", say("ux300_android_rename_more", 1))
        assertEquals("И ещё 12 пар", say("ux300_android_rename_more", 12))
        assertEquals("И ещё 22 пары", say("ux300_android_rename_more", 22))
        assertEquals("Показать ещё 1 задание", say("ux30_study_show_more_homework", 1))
        assertEquals("Показать ещё 5 заданий", say("ux30_study_show_more_homework", 5))
        assertEquals("Ближайшие сроки: 1 задание", say("deadlines_title_count", 1))
        assertEquals("Ближайшие сроки: 3 задания", say("deadlines_title_count", 3))
        assertEquals("Ближайшие сроки: 11 заданий", say("deadlines_title_count", 11))
    }

    @Test fun old_single_form_strings_are_gone_and_calls_use_plurals() {
        val all = res.listFiles()!!.joinToString("\n") { it.readText() }
        listOf("Показать ещё %1\$d заданий", "активных</string>", "осталось %1\$d символов", "И ещё %1\$d пар</string>",
            "Ближайшие сроки · %1\$s/%2\$s").forEach { assertFalse(it, all.contains(it)) }
        val src = File("src/main/java").walkTopDown().filter { it.extension == "kt" }.joinToString("\n") { it.readText() }
        assertFalse(src.contains("R.string.channel_ballot_count"))
        assertTrue(src.contains("R.plurals.channel_ballot_count"))
        assertTrue(src.contains("R.plurals.deadlines_title_count"))
    }

    @Test fun empty_day_uses_glossary() {
        // После #97 «Пар нет» — строка общего каталога (design/strings/ru.json, emptyDay), Android-имя schedule_empty_title.
        assertTrue(File(res, "strings_catalog.xml").readText().contains("<string name=\"schedule_empty_title\">Пар нет</string>"))
        assertFalse(File(res, "strings_schedule_maps_presentation.xml").readText().contains("schedule_empty_title"))
    }

    /** AN-28: перечисленные подписи больше не литералы в Kotlin. */
    @Test fun an28_labels_are_resources_not_kotlin_literals() {
        val social = File("src/main/java/ru/bgtu_voenmeh/zapara/data/social/SocialHttpClient.kt").readText()
        val account = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/account/AccountUi.kt").readText()
        listOf("\"Личный чат", "\"Учебная группа\"").forEach { assertFalse(it, social.contains(it)) }
        listOf("\"Веб\"", "\"Устройство\"").forEach { assertFalse(it, account.contains(it)) }
        val xml = File(res, "strings_plurals_an07.xml").readText()
        listOf(">Личный чат<", ">Личный чат · %1\$s<", ">Учебная группа<", ">Веб<", ">Устройство<").forEach { assertTrue(it, xml.contains(it)) }
        assertTrue(File("src/main/java/ru/bgtu_voenmeh/zapara/ui/inbox/InboxSection.kt").readText().contains("internal fun inboxSubtitle(row: InboxRow)"))
    }

    /** #106, критерий «нет литералов кириллицы в перечисленных Kotlin-файлах»: и внутренние сообщения исключений — на английском. */
    @Test fun listed_kotlin_files_have_no_cyrillic_literals() {
        val cyrillic = Regex("\"[^\"\\n]*\\p{IsCyrillic}[^\"\\n]*\"")
        listOf("src/main/java/ru/bgtu_voenmeh/zapara/data/social/SocialHttpClient.kt", "src/main/java/ru/bgtu_voenmeh/zapara/ui/account/AccountUi.kt").forEach { path ->
            val hits = File(path).readLines().withIndex()
                .filter { (_, line) -> !line.trimStart().startsWith("//") && !line.trimStart().startsWith("*") && cyrillic.containsMatchIn(line) }
                .map { (i, _) -> "$path:${i + 1}" }
            assertTrue(hits.joinToString(), hits.isEmpty())
        }
    }
}
