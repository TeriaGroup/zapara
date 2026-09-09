package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

class GuardsTest {
    private val main = File("src/main")
    private val legacy = emptySet<String>()

    @Test fun button_indication_obeys_app_motion_switch() {
        val source = File(main, "java/ru/bgtu_voenmeh/zapara/ui/theme/Primitives.kt").readText()
        assertTrue("Button feedback must be gated by the app preference, not just OS scale",
            source.contains("indication = if (Zapara.motion.enabled)"))
    }

    @Test fun new_ui_uses_tokens_not_raw_colors_or_monospace() {
        File(main,"java/ru/bgtu_voenmeh/zapara/ui").walkTopDown().filter { it.extension == "kt" && it.name !in legacy }.forEach {
            val source = it.readText()
            assertFalse(it.path, source.contains("FontFamily.Monospace"))
            if (it.name != "Tokens.kt") assertFalse(it.path, Regex("Color\\s*\\(\\s*0[xX]").containsMatchIn(source))
        }
        assertFalse(File("build.gradle.kts").readText().contains("material-icons-extended"))
    }

    @Test fun russian_only_resources() {
        assertFalse(File(main,"res").listFiles()!!.any { it.name.startsWith("values-en") })
        val doc = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(File(main,"res/values/strings.xml"))
        val strings = doc.getElementsByTagName("string")
        assertTrue(strings.length > 0)
        val cyrillic = Regex("[А-Яа-яЁё]")
        val latin = Regex("[A-Za-z]")
        val placeholder = Regex("%\\d+\\$[sd]|%[sd]")
        for (i in 0 until strings.length) {
            val text = strings.item(i).textContent
            val stripped = placeholder.replace(text, "")
            assertTrue(text, cyrillic.containsMatchIn(text) || !latin.containsMatchIn(stripped))
        }
    }

    @Test fun ui_kotlin_has_no_raw_user_facing_cyrillic() {
        val dataTokens = setOf("ГК", "УЛК", "ВЦ", "лек", "пр", "лаб", "конс", "зач", "экз", "курс", "практика", "дистанционно")
        val quote = Regex("\"((?:\\\\.|[^\"\\\\])*)\"")
        val cyrillic = Regex("[А-Яа-яЁё]")
        val offenders = mutableListOf<String>()
        File(main, "java/ru/bgtu_voenmeh/zapara/ui").walkTopDown().filter { it.extension == "kt" }.forEach { file ->
            val stripped = file.readText()
                .replace(Regex("/\\*[\\s\\S]*?\\*/"), "")
                .replace(Regex("//.*"), "")
            quote.findAll(stripped).forEach { match ->
                val text = match.groupValues[1].replace("\\\"", "\"").replace("\\\\", "\\")
                if (cyrillic.containsMatchIn(text) && text !in dataTokens) {
                    offenders += "${file.name}: \"$text\""
                }
            }
        }
        assertTrue("User-facing copy must go through R.string / UiCopy:\n${offenders.joinToString("\n")}", offenders.isEmpty())
    }

    @Test fun all_27_vectors_match_desktop_geometry() {
        val desktop = File("../../src/Vograph.Desktop/Theme/Icons.axaml").readText()
        val names = listOf("Calendar","Week","Summary","Teachers","Map","Friends","Homework","Settings","Sun","Moon",
            "ChevronLeft","ChevronRight","Pencil","Plus","Minus","MapPin","X","Check","Trash","Search","Refresh","Alert","Users","Download","ExternalLink","Fullscreen","Menu")
        for (name in names) {
            val snake = name.replace(Regex("([a-z])([A-Z])"), "$1_$2").lowercase()
            val vector = File(main,"res/drawable/ic_$snake.xml").readText()
            val geometry = desktop.substringAfter("x:Key=\"Icon.$name\">").substringBefore("</StreamGeometry>")
            assertTrue(name, vector.contains("android:pathData=\"$geometry\""))
            listOf("strokeWidth=\"1.75\"","viewportWidth=\"24\"","viewportHeight=\"24\"", "fillColor=\"#00000000\"", "strokeLineCap=\"round\"", "strokeLineJoin=\"round\"").forEach {
                assertTrue("$name $it", vector.contains(it))
            }
        }
    }

    @Test fun local_fonts_and_licenses_present() {
        listOf("regular","medium","semibold").forEach {
            val bytes = File(main,"res/font/inter_$it.ttf").readBytes()
            assertTrue(bytes.size > 10000)
            assertArrayEquals(byteArrayOf(0,1,0,0), bytes.take(4).toByteArray())
        }
        assertTrue(File(main,"assets/fonts/OFL.txt").readText().contains("SIL OPEN FONT LICENSE Version 1.1"))
        assertTrue(File(main,"assets/icons/LICENSE.txt").readText().contains("ISC License"))
    }
}
