package ru.bgtu_voenmeh.zapara

import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryComposer

/** Also compiled from this exact source by the local host init script; no Compose renderer. */
class SummaryCaptureModelTest {
    private val viewport = SummaryBounds(0f, 0f, 360f, 640f)
    private fun frame(filter: String = "odd", id: String = "f1") =
        SummaryFrameKey(filter, "dark", "1.0", "phone360-portrait", "build-sha", id, viewport)
    private fun row(id: String, key: SummaryFrameKey = frame(), bounds: SummaryBounds = viewport,
        asserted: Boolean = true) = SummaryObservation(id, key, listOf(bounds), asserted)

    @Test fun positive_window_origin_keeps_lower_targets_and_rejects_above_root_targets() {
        val root = summaryRootWindowBounds(12f, 24f, 360f, 640f)
        assertEquals(SummaryBounds(12f, 24f, 372f, 664f), root)
        assertTrue(root.contains(SummaryBounds(20f, 620f, 360f, 650f)))
        assertFalse(root.contains(SummaryBounds(20f, 10f, 360f, 30f)))
        val key = frame().copy(viewport = root)
        val ledger = SummaryFrameLedger()
        ledger.credit(key, listOf(row("day-1", key, SummaryBounds(20f, 620f, 360f, 650f))))
        assertEquals(43, ledger.pending.size)
    }

    @Test fun negative_window_origin_keeps_upper_targets_and_rejects_below_root_targets() {
        val root = summaryRootWindowBounds(-12f, -24f, 360f, 640f)
        assertEquals(SummaryBounds(-12f, -24f, 348f, 616f), root)
        assertTrue(root.contains(SummaryBounds(-10f, -20f, 300f, 10f)))
        assertFalse(root.contains(SummaryBounds(0f, 600f, 300f, 630f)))
        assertFalse(root.contains(SummaryBounds(340f, 20f, 360f, 40f)))
    }

    @Test fun translated_root_does_not_waive_parent_clip_or_partial_row_guards() {
        listOf(24f, -24f).forEach { origin ->
            val root = summaryRootWindowBounds(origin, origin, 360f, 640f)
            val clippedList = SummaryBounds(origin + 10f, origin + 40f, origin + 350f, origin + 600f)
            val fullRow = SummaryBounds(origin + 20f, origin + 50f, origin + 340f, origin + 80f)
            val partialRow = SummaryBounds(origin + 20f, origin + 590f, origin + 340f, origin + 620f)
            val horizontalClip = SummaryBounds(origin + 5f, origin + 50f, origin + 340f, origin + 80f)
            assertTrue(root.contains(fullRow))
            assertTrue(clippedList.contains(fullRow))
            assertTrue(root.contains(partialRow))
            assertFalse(clippedList.contains(partialRow))
            assertTrue(clippedList.crossesVerticalBoundary(partialRow))
            assertFalse(clippedList.contains(horizontalClip))
            assertFalse(clippedList.crossesVerticalBoundary(horizontalClip))
            assertFalse(clippedList.crossesVerticalBoundary(fullRow))
        }
    }

    @Test fun translated_viewport_identity_and_partial_root_credit_fail_closed() {
        val root = summaryRootWindowBounds(12f, 24f, 360f, 640f)
        val key = frame().copy(viewport = root)
        val ledger = SummaryFrameLedger()
        val moved = key.copy(viewport = summaryRootWindowBounds(12f, 25f, 360f, 640f))
        assertThrows(IllegalArgumentException::class.java) {
            ledger.credit(key, listOf(row("day-1", moved, SummaryBounds(20f, 40f, 100f, 60f))))
        }
        assertThrows(IllegalArgumentException::class.java) {
            ledger.credit(key, listOf(row("day-1", key, SummaryBounds(20f, 10f, 100f, 30f))))
        }
        assertEquals(44, ledger.pending.size)
    }

    @Test fun bottom_driver_reads_live_forward_state_even_after_last_row_is_visible() {
        var step = 0
        val end = driveSummaryBottom(4, { SummaryScroll(step.toFloat(), 3f, step < 3) }, { step++ })
        assertEquals(3, step)
        assertFalse(end.canScrollForward)
    }

    @Test fun bottom_driver_fails_on_stall_and_bounded_exhaustion() {
        assertThrows(IllegalStateException::class.java) {
            driveSummaryBottom(4, { SummaryScroll(1f, 5f, true) }, {})
        }
        var step = 0
        assertThrows(IllegalStateException::class.java) {
            driveSummaryBottom(2, { SummaryScroll(step.toFloat(), 20f, true) }, { step++ })
        }
        assertEquals(2, step)
        assertEquals(SummaryScroll(0f, 0f, false),
            driveSummaryBottom(1, { SummaryScroll(0f, 0f, false) }, { fail("Already at end") }))
    }

    @Test fun grouping_credits_only_observed_asserted_full_rows_and_keeps_rest_pending() {
        val ledger = SummaryFrameLedger()
        val group = ledger.credit(frame(), listOf(row("day-1"), row("day-2"),
            row("day-3", bounds = SummaryBounds(0f, 620f, 360f, 660f)), row("day-4", asserted = false)))
        assertEquals(listOf("day-1", "day-2"), group.map { it.obligation })
        assertEquals(42, ledger.pending.size)
        assertTrue(ledger.pending.contains("odd-day-3"))
        assertTrue(ledger.pending.contains("even-day-1"))
    }

    @Test fun grouping_rejects_unknown_empty_duplicate_and_mismatched_frame_inputs_atomically() {
        val ledger = SummaryFrameLedger()
        listOf(emptyList(), listOf(row("unknown")), listOf(row("day-1"), row("day-1")),
            listOf(row("day-1", frame("even"))), listOf(row("day-1", frame(id = "f2"))),
            listOf(row("day-1", frame().copy(viewport = SummaryBounds(0f, 0f, 640f, 360f)))),
            listOf(row("day-1", frame().copy(build = "other"))),
            listOf(row("day-1", frame().copy(theme = "light"))),
            listOf(row("day-1", frame().copy(scale = "2.0"))),
            listOf(row("day-1").copy(bounds = emptyList())),
            listOf(row("day-1", asserted = false))).forEach { observations ->
            assertThrows(IllegalArgumentException::class.java) { ledger.credit(frame(), observations) }
            assertEquals(44, ledger.pending.size)
        }
        ledger.credit(frame(), listOf(row("day-1")))
        assertThrows(IllegalArgumentException::class.java) { ledger.credit(frame(), listOf(row("day-1"))) }
        assertEquals(43, ledger.pending.size)
    }

    @Test fun metadata_and_bounds_reject_unknown_or_nonfinite_inputs() {
        listOf(frame().copy(filter = "unknown"), frame().copy(theme = "sepia"),
            frame().copy(scale = "1.3"), frame().copy(configuration = "unknown"),
            frame().copy(build = ""), frame().copy(frame = "")).forEach { key ->
            assertThrows(IllegalArgumentException::class.java) { SummaryFrameLedger().credit(key, listOf(row("day-1", key))) }
        }
        assertThrows(IllegalArgumentException::class.java) { SummaryBounds(0f, Float.NaN, 1f, 2f) }
        assertThrows(IllegalArgumentException::class.java) { SummaryBounds(0f, 0f, 0f, 2f) }
        assertThrows(IllegalArgumentException::class.java) { SummaryScroll(Float.NaN, 2f, true) }
    }

    @Test fun all_24_configurations_retain_44_pending_obligations_without_observations() {
        val configurations = listOf("phone360-portrait", "phone360-landscape", "tablet600-portrait", "tablet600-landscape")
        var count = 0
        configurations.forEach { configuration -> listOf("dark", "light").forEach { theme ->
            listOf("1.0", "1.5", "2.0").forEach { scale ->
                val ledger = SummaryFrameLedger()
                assertEquals(44, ledger.pending.size)
                val key = frame().copy(configuration = configuration, theme = theme, scale = scale)
                ledger.credit(key, listOf(row("day-1", key)))
                assertEquals(43, ledger.pending.size)
                count++
            }
        } }
        assertEquals(24, count) // Contract inputs, never runtime/configuration coverage.
    }

    @Test fun boundary_scope_excludes_only_vertical_lazy_boundaries_not_visible_parent_clips() {
        assertTrue(viewport.crossesVerticalBoundary(SummaryBounds(10f, -10f, 100f, 20f)))
        assertTrue(viewport.crossesVerticalBoundary(SummaryBounds(10f, 630f, 100f, 650f)))
        assertFalse(viewport.crossesVerticalBoundary(SummaryBounds(10f, 10f, 100f, 30f)))
        assertFalse(viewport.crossesVerticalBoundary(SummaryBounds(-10f, 10f, 100f, 30f)))
    }

    private val copy = UiCopy { key, _ ->
        check(key == "type_lecture") { "Unexpected fixture copy key: $key" }
        "лекция"
    }

    @Test fun parity_input_is_exact_and_not_preaggregated() {
        assertEquals(listOf(1 to 1, 1 to 2, 1 to 0, 2 to 1),
            SummaryCaptureModel.lessons.map { it.dayOfWeek to it.parity })
        assertEquals(listOf("101;", "102;", "101;", "—;"),
            SummaryCaptureModel.lessons.map { it.classroomRaw })
    }

    @Test fun all_filters_match_literal_totals_and_every_breakdown_row() {
        SummaryCaptureModel.cases.forEach { expected ->
            val state = SummaryCaptureModel.state(expected.segment, copy)
            assertTrue(state.loaded)
            assertTrue(state.hasGroup)
            assertEquals(expected.segment, state.segment)
            val tiles = state.tiles
            assertEquals(expected.total, tiles.total)
            assertEquals(expected.days.mapIndexed { index, n -> index + 1 to n }, tiles.byDay)
            assertEquals(listOf("лекция" to expected.total), tiles.byType)
            assertEquals(listOf("Математика" to expected.total), tiles.bySubject)
            assertEquals(listOf("Иванов" to expected.total), tiles.byTeacher)
            assertEquals(expected.rooms, tiles.byRoom)
            assertEquals(expected.rooms.map { it.first }, tiles.rooms)
            println(listOf("SUMMARY_FIXTURE", expected.id, state.segment, tiles.total,
                tiles.byDay.joinToString(";") { "${it.first}=${it.second}" },
                tiles.byType.joinToString(";") { "${it.first}=${it.second}" },
                tiles.bySubject.joinToString(";") { "${it.first}=${it.second}" },
                tiles.byTeacher.joinToString(";") { "${it.first}=${it.second}" },
                tiles.byRoom.joinToString(";") { "${it.first}=${it.second}" },
                expected.substates.joinToString(",")).joinToString("|"))
        }
    }

    @Test fun absent_room_keeps_lesson_but_never_creates_building_only_row() {
        listOf("", "   ", "—", "—;").forEach { absent ->
            val lessons = SummaryCaptureModel.lessons.mapIndexed { index, lesson ->
                if (index == 3) lesson.copy(classroomRaw = absent) else lesson
            }
            SummaryCaptureModel.cases.forEach { expected ->
                val tiles = SummaryComposer.tiles(expected.segment, lessons, { _, _ -> "" }, copy)
                assertEquals(expected.total, tiles.total)
                assertEquals(expected.rooms, tiles.byRoom)
                assertFalse(tiles.rooms.contains("ГК"))
            }
        }
    }

    @Test fun invalid_segments_fail_instead_of_rendering_both() {
        listOf(-1, 3, Int.MIN_VALUE, Int.MAX_VALUE).forEach { segment ->
            assertThrows(IllegalArgumentException::class.java) { SummaryCaptureModel.state(segment, copy) }
        }
    }

    @Test fun capture_substates_explicitly_cover_all_known_rows_without_empty_room_state() {
        val states = SummaryCaptureModel.cases.flatMap { c -> c.substates.map { "${c.id}-$it" } }
        assertEquals(44, states.size)
        assertEquals(states.size, states.toSet().size)
        SummaryCaptureModel.cases.forEach { c ->
            assertEquals(listOf("top", "day", "day-1", "day-2", "day-3", "day-4", "day-5", "day-6",
                "type", "subject", "teacher", "room") + c.rooms.indices.map { "room-$it" } + "bottom", c.substates)
            assertFalse(c.substates.contains("empty-rooms"))
        }
    }
}
