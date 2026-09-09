package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.*
import org.junit.Test
import java.io.File

/** Every animation duration in main source must go through LocalMotion. */
class MotionGuardTest {
    private val main = File("src/main")
    private val apis = listOf(
        Regex("""(?<![A-Za-z])tween\("""),
        Regex("""infiniteRepeatable\("""),
        Regex("""animateFloatAsState\("""),
        Regex("""animateDpAsState\("""),
        Regex("""animateColorAsState\(""")
    )

    @Test fun animation_calls_use_motion_ms() {
        val violators = mutableListOf<String>()
        main.walkTopDown().filter { it.extension == "kt" }.forEach { file ->
            file.readLines().forEachIndexed { i, raw ->
                val line = raw.trim()
                if (line.startsWith("//") || line.startsWith("*") || line.startsWith("/*")) return@forEachIndexed
                if (apis.none { it.containsMatchIn(line) }) return@forEachIndexed
                val ok = line.contains("motion.ms(") || line.contains("Zapara.motion.ms(") || line.contains("ms(Durations.")
                if (!ok) violators += "${file.name}:${i + 1}: $line"
            }
        }
        assertTrue(violators.joinToString("\n"), violators.isEmpty())
    }

    @Test fun animate_content_size_uses_motion() {
        val violators = mutableListOf<String>()
        main.walkTopDown().filter { it.extension == "kt" }.forEach { file ->
            file.readLines().forEachIndexed { i, raw ->
                val line = raw.trim()
                if (!line.contains("Modifier.animateContentSize(") && !line.contains(".animateContentSize(")) return@forEachIndexed
                val ok = line.contains("motion.ms(") || line.contains("Zapara.motion.ms(") || line.contains("ms(Durations.")
                if (!ok) violators += "${file.name}:${i + 1}: $line"
            }
        }
        assertTrue(violators.joinToString("\n"), violators.isEmpty())
    }

    @Test fun infinite_transitions_are_gated() {
        val violators = mutableListOf<String>()
        main.walkTopDown().filter { it.extension == "kt" }.forEach { file ->
            val text = file.readText()
            if (!text.contains("rememberInfiniteTransition(")) return@forEach
            val functions = text.split(Regex("@Composable"))
            functions.forEach { fn ->
                if (!fn.contains("rememberInfiniteTransition(")) return@forEach
                if (!fn.contains("motion.enabled")) {
                    violators += "${file.name} rememberInfiniteTransition without motion.enabled"
                }
            }
        }
        assertTrue(violators.joinToString("\n"), violators.isEmpty())
    }

    @Test fun appear_does_not_override_talkback_description() {
        val source = File(main, "java/ru/bgtu_voenmeh/zapara/ui/theme/Appear.kt").readText()
        assertFalse("appear() must not set contentDescription to the alpha float", source.contains("contentDescription"))
        assertFalse("appear() must not set stateDescription to the alpha float", source.contains("stateDescription"))
        assertTrue("AppearAlphaKey stays for Compose tests", source.contains("AppearAlphaKey"))
    }

    @Test fun schedule_pager_snap_uses_motion_ms() {
        val source = File(main, "java/ru/bgtu_voenmeh/zapara/ui/schedule/ScheduleSection.kt").readText()
        assertTrue("user swipe must go through PagerDefaults.flingBehavior", source.contains("PagerDefaults.flingBehavior"))
        val snap = source.lineSequence().map { it.trim() }.firstOrNull { it.contains("snapAnimationSpec") }
        assertNotNull("snapAnimationSpec required so the pager spring can be zeroed", snap)
        assertTrue("snapAnimationSpec must use motion.ms: $snap",
            snap!!.contains("motion.ms(") || snap.contains("Zapara.motion.ms(") || snap.contains("ms(Durations."))
    }
}
