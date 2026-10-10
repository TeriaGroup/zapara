package ru.bgtu_voenmeh.zapara.ui

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/** #12: русские строки Android по глоссарию docs/glossary.md и общему каталогу design/strings/ru.json. */
class GlossaryStringsTest {
    private val values = File("src/main/res/values")
    private val strings: Map<String, String> by lazy {
        val pattern = Regex("""<string\s+name="([^"]+)"[^>]*>(.*?)</string>""", RegexOption.DOT_MATCHES_ALL)
        values.listFiles { file -> file.extension == "xml" }!!.flatMap { file ->
            pattern.findAll(file.readText()).map { it.groupValues[1] to it.groupValues[2] }.toList()
        }.toMap()
    }

    /** Поисковые синонимы настроек намеренно содержат написания без «ё» («учеба», «четность»). */
    private fun isSearchSynonyms(name: String) = name.startsWith("uxnext_settings_terms_")

    private fun violations(rule: Regex) = strings.filter { (name, value) -> !isSearchSynonyms(name) && rule.containsMatchIn(value) }.keys.sorted()

    @Test fun parity_is_written_with_yo() = assertEquals(emptyList<String>(), violations(Regex("""(^|[^ёЁА-Яа-я])[Нн]?[Чч]етн""")))

    @Test fun done_is_vypolneno_not_gotovo_u_menya() = assertEquals(emptyList<String>(), violations(Regex("""[Гг]отово у меня|\bсдано\b""")))

    @Test fun lessons_are_pary_in_the_interface() = assertEquals(emptyList<String>(), violations(Regex("""(^|[^А-Яа-яЁё])[Зз]аняти[еяийюм]""")))

    @Test fun ellipsis_is_one_character() = assertEquals(emptyList<String>(), violations(Regex("""\.\.\.""")))

    @Test fun catalog_strings_are_generated_not_duplicated() {
        val catalog = File(values, "strings_catalog.xml").readText()
        assertTrue(catalog.contains("Сгенерировано scripts/design/strings.mjs"))
        assertEquals("Расписание военмех", strings["settings_about_title"])
        assertEquals("нечётная", strings["notification_odd"])
        assertEquals("чётная", strings["notification_even"])
        val others = values.listFiles { file -> file.extension == "xml" && file.name != "strings_catalog.xml" }!!.joinToString("\n") { it.readText() }
        Regex("""<string\s+name="([^"]+)"""").findAll(catalog).forEach { assertTrue(it.groupValues[1], !others.contains("name=\"${it.groupValues[1]}\"")) }
    }
}
