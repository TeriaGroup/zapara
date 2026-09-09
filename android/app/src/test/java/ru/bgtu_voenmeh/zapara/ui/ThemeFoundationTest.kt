package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import java.io.File

/** Source guards also run RED before the public types exist. */
class ThemeFoundationTest {
    private val theme = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/theme")

    @Test fun tokens_contract_exists() {
        assertTrue("Task1 Tokens.kt required", File(theme, "Tokens.kt").isFile)
    }

    @Test fun theme_choice_contract_exists() {
        assertTrue(File(theme, "Theme.kt").readText().contains("enum class ThemeChoice"))
    }

    @Test fun motion_contract_exists() {
        assertTrue("Task1 Motion.kt required", File(theme, "Motion.kt").isFile)
    }

    @Test fun no_monospace_in_foundation() {
        theme.walkTopDown().filter { it.extension == "kt" }.forEach {
            assertFalse(it.path, it.readText().contains("FontFamily.Monospace"))
        }
    }
}
