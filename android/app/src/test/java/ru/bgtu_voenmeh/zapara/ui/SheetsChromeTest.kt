package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class SheetsChromeTest {
    @Test fun bottom_sheet_covers_the_nav_bar() {
        val src = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/components/Controls.kt").readText()
        assertTrue(
            "homework/route sheets must cover the bottom nav like the group picker",
            src.contains("Dialog(") && src.contains("ZBottomSheet")
        )
    }
}
