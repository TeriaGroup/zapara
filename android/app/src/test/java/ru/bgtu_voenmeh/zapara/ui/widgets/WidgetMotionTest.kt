package ru.bgtu_voenmeh.zapara.ui.widgets

import org.junit.Assert.*
import org.junit.Test

class WidgetMotionTest {
    private val a = WidgetJobIdentity("account-A", "db-a", 1)

    @Test fun phase_arc_finishes_old_arc_then_settles_to_new_with_finite_halo() {
        assertEquals(PhaseArcFrame(0.25f, 0f), phaseArc(0.25f, 0.75f, 0f))
        assertEquals(1f, phaseArc(0.25f, 0.75f, 0.35f).fraction, 0.001f)
        assertTrue(phaseArc(0.25f, 0.75f, 0.35f).haloAlpha > 0f)
        assertEquals(PhaseArcFrame(0.75f, 0f), phaseArc(0.25f, 0.75f, 1f))
    }

    @Test fun room_with_letters_leaves_up_and_enters_from_below_and_heartbeat_is_still() {
        val old = WayfinderWidgetSnapshot(a, "Куда идти", "Сейчас", "Пара", "09:00", "493а ГК",
            "493а", java.time.LocalDate.of(2026, 9, 23), true, null)
        val enabled = WidgetMotionPolicy.of(true, 1f, true)
        assertNull(WidgetFaceEffects.room(old, old, enabled))
        val scene = WidgetFaceEffects.room(old, old.copy(room = "201 Б"), enabled)!!
        assertEquals("493а ГК", scene.oldRoom)
        assertEquals(WidgetMotionPose(0f, 1f, 12f, 0f), roomReel(0f))
        assertEquals(WidgetMotionPose(-12f, 0f, 0f, 1f), roomReel(1f))
        // At bezier parameter 0.5: x = 0.275 and y = 0.8.
        val middle: Any = roomReel(0.275f)
        assertTrue(middle is WidgetMotionPose)
        middle as WidgetMotionPose
        assertEquals(-9.6f, middle.oldOffsetYDp, 0.001f)
        assertEquals(0.2f, middle.oldAlpha, 0.001f)
        assertEquals(2.4f, middle.newOffsetYDp, 0.001f)
        assertEquals(0.8f, middle.newAlpha, 0.001f)
        assertNull(WidgetFaceEffects.room(old, old.copy(room = "201", identity = a.copy(generation = 2)), enabled))
        assertNull(WidgetFaceEffects.room(old, old.copy(room = "201"), WidgetMotionPolicy.Disabled))
    }

    @Test fun week_row_crossing_fades_without_diagonal_motion_and_only_changed_counts_roll() {
        val start = weekMarker(6, 0, 0f)
        assertEquals(WeekMarkerFrame(1f, 0f, 0f), start)
        assertEquals(WeekMarkerFrame(0f, 0f, 0f), weekMarker(6, 0, 0.5f))
        assertEquals(WeekMarkerFrame(0f, 1f, 0f), weekMarker(6, 0, 1f))
        assertTrue(weekMarker(0, 1, 0.2f).offsetXDp > 0f)
        val date = java.time.LocalDate.of(2026, 9, 21)
        val old = WeekWidgetSnapshot(a, "Неделя", "Группа", (0..6).map {
            WeekWidgetDay(date.plusDays(it.toLong()), "День", 2, it == 0)
        }, null)
        val enabled = WidgetMotionPolicy.of(true, 1f, true)
        assertNull(WidgetFaceEffects.week(old, old, enabled))
        val scene = WidgetFaceEffects.week(old, old.copy(days = old.days.mapIndexed { i, day ->
            day.copy(lessonCount = if (i == 3) 4 else 2)
        }), enabled)!!
        assertEquals(setOf(3), scene.changedCountIndices)
        assertNull(WidgetFaceEffects.week(old, old.copy(identity = a.copy(profileId = "B")), enabled))
    }

    @Test fun oversized_row_bitmap_preserves_dp_geometry_on_both_axes() {
        listOf(280 to 160, 160 to 280).forEach { (widthDp, heightDp) ->
            val frame = widgetRowBitmapGeometry(widthDp, heightDp, 3f)
            assertTrue(frame.widthPx <= 640 && frame.heightPx <= 640)
            assertEquals(640, maxOf(frame.widthPx, frame.heightPx))
            val hostWidthPx = widthDp * 3f
            val hostHeightPx = heightDp * 3f
            val baselineDp = 64f
            val onHostX = baselineDp * frame.pixelsPerDp * hostWidthPx / frame.widthPx / 3f
            val onHostY = baselineDp * frame.pixelsPerDp * hostHeightPx / frame.heightPx / 3f
            assertEquals(baselineDp, onHostX, 0.15f)
            assertEquals(baselineDp, onHostY, 0.15f)
        }
    }

    @Test fun completion_accent_requires_done_id_and_deletion_only_fades() {
        val old = HomeworkWidgetRow("Матан", "Сдать", "warn", 41)
        val next = HomeworkWidgetRow("Физика", "Читать", "text2", 42)
        val before = HomeworkWidgetSnapshot(a, "Домашка", "", null, listOf(old, next))
        val policy = WidgetMotionPolicy.of(true, 1f, true)
        val completed = WidgetRowEffects.homework(before, before.copy(rows = listOf(next)), setOf(41), policy)!!
        assertEquals(WidgetMotionKind.HomeworkCompleted, completed.kind)
        assertTrue(completed.accentAlphaAt(0.5f) > 0f)
        assertEquals(0f, completed.accentAlphaAt(1f))
        val deleted = WidgetRowEffects.homework(before, before.copy(rows = listOf(next)), emptySet(), policy)!!
        assertEquals(WidgetMotionKind.HomeworkShift, deleted.kind)
        assertEquals(0f, deleted.accentAlphaAt(0.5f))
        assertNull(WidgetRowEffects.homework(before, before, setOf(41), policy))
        val reordered = WidgetRowEffects.homework(before, before.copy(rows = listOf(next, old)), emptySet(), policy)
        assertEquals(WidgetMotionKind.HomeworkShift, reordered?.kind)
    }

    @Test fun schedule_cascade_limits_to_three_visible_rows_and_staggers_arrival() {
        val old = ScheduleWidgetRow("Старая", "09:00", true, 1)
        val upcoming = (2..4).map { ScheduleWidgetRow("Пара $it", "10:00", false, it) }
        val before = ScheduleWidgetSnapshot(a, "Расписание", "", null, listOf(old) + upcoming)
        val after = before.copy(rows = upcoming, toss = old)
        val scene = WidgetRowEffects.schedule(before, after, WidgetMotionPolicy.of(true, 1f, true))!!
        assertEquals(3, scene.accentCount)
        assertEquals(0f, scene.rowAlphaAt(0f, 0))
        assertEquals(0f, scene.rowAlphaAt(0f, 1))
        assertTrue(scene.rowAlphaAt(0.2f, 0) > scene.rowAlphaAt(0.2f, 1))
        assertEquals(1f, scene.rowAlphaAt(1f, 2))
    }

    @Test fun policy_observations_cannot_arrive_out_of_order_and_reenable_motion() {
        val policies = WidgetMotionPolicies()
        val oldRead = policies.beginObservation()
        val newRead = policies.beginObservation()
        assertTrue(policies.update(a, WidgetMotionPolicy.Disabled, newRead))
        assertFalse(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), oldRead))
        assertFalse(policies.allows(a))
    }

    @Test fun a_pending_setting_write_blocks_reads_until_a_fresh_post_write_observation() {
        val policies = WidgetMotionPolicies()
        val beforeWrite = policies.beginObservation()
        val change = policies.beginChange(a)
        val duringWrite = policies.beginObservation()
        assertFalse(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), duringWrite))
        assertFalse(policies.allows(a))
        policies.finishChange(a, change)
        assertFalse(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), beforeWrite))
        assertFalse(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), duringWrite))
        assertFalse(policies.allows(a))
        assertTrue(policies.update(a, WidgetMotionPolicy.Disabled, policies.beginObservation()))
        assertFalse(policies.allows(a))
        assertTrue(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), policies.beginObservation()))
        assertTrue(policies.allows(a))
    }

    @Test fun finishing_an_older_write_cannot_release_a_newer_pending_preference() {
        val policies = WidgetMotionPolicies()
        val older = policies.beginChange(a)
        val newer = policies.beginChange(a)
        policies.finishChange(a, older)
        assertFalse(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), policies.beginObservation()))
        assertFalse(policies.allows(a))
        policies.finishChange(a, newer)
        assertTrue(policies.update(a, WidgetMotionPolicy.of(true, 1f, true), policies.beginObservation()))
        assertTrue(policies.allows(a))
    }

    @Test fun disabled_motion_is_an_immediate_final_state() {
        listOf(
            WidgetMotionPolicy.of(false, 1f, true),
            WidgetMotionPolicy.of(true, 0f, true),
            WidgetMotionPolicy.of(true, 1f, false),
            WidgetMotionPolicy.of(true, -1f, true),
            WidgetMotionPolicy.of(true, Float.NaN, true)
        ).forEach {
            assertFalse(it.enabled)
            assertEquals(0L, it.durationMs)
            assertEquals(1, it.frameCount)
            assertEquals(listOf(WidgetMotionFrame(0, 1f)), it.frames())
        }
    }

    @Test fun enabled_motion_has_seven_frames_and_finishes_within_half_a_second() {
        listOf(0.1f to 240L, 1f to 420L, 10f to 500L).forEach { (scale, duration) ->
            val policy = WidgetMotionPolicy.of(true, scale, true)
            assertTrue(policy.enabled)
            assertEquals(duration, policy.durationMs)
            assertEquals(7, policy.frameCount)
            val frames = policy.frames()
            assertEquals(7, frames.size)
            assertEquals(WidgetMotionFrame(0, 0f), frames.first())
            assertEquals(WidgetMotionFrame(duration, 1f), frames.last())
            assertTrue(frames.zipWithNext().all { (left, right) -> right.delayMs > left.delayMs && right.progress > left.progress })
        }
    }

    @Test fun pose_has_exact_endpoints_and_clamps_progress() {
        val start = WidgetMotionPose(0f, 1f, 12f, 0f)
        val end = WidgetMotionPose(-12f, 0f, 0f, 1f)
        assertEquals(start, widgetMotionPose(0f))
        assertEquals(start, widgetMotionPose(-1f))
        assertEquals(end, widgetMotionPose(1f))
        assertEquals(end, widgetMotionPose(2f))
    }

    @Test fun pose_fades_monotonically_and_solves_the_project_cubic_curve() {
        val poses = (0..100).map { widgetMotionPose(it / 100f) }
        assertTrue(poses.zipWithNext().all { (left, right) ->
            right.oldAlpha <= left.oldAlpha && right.newAlpha >= left.newAlpha &&
                right.oldOffsetYDp <= left.oldOffsetYDp && right.newOffsetYDp <= left.newOffsetYDp
        })
        // At Bezier parameter 0.5 the project's x = 0.275 and y = 0.8.
        assertEquals(0.8f, widgetMotionPose(0.275f).newAlpha, 0.0001f)
        assertEquals(-9.6f, widgetMotionPose(0.275f).oldOffsetYDp, 0.002f)
    }

    @Test fun replacing_a_widget_token_does_not_invalidate_a_different_widget() {
        val tokens = WidgetMotionTokens()
        val first = tokens.next(7)
        val other = tokens.next(8)
        val second = tokens.next(7)
        assertTrue(second > first)
        assertFalse(tokens.isCurrent(7, first))
        assertTrue(tokens.isCurrent(7, second))
        assertTrue(tokens.isCurrent(8, other))
        assertFalse(tokens.isCurrent(9, other))
    }

    @Test fun frames_require_matching_profile_database_and_generation() {
        val tokens = WidgetMotionTokens()
        val token = tokens.next(7)
        assertTrue(tokens.mayDraw(7, token, a, a))
        listOf(a.copy(profileId = "account-B"), a.copy(databaseName = "db-b"), a.copy(generation = 2)).forEach {
            assertFalse(tokens.mayDraw(7, token, a, it))
        }
        tokens.next(7)
        assertFalse(tokens.mayDraw(7, token, a, a))
    }

    @Test fun removed_ids_are_pruned_without_resurrecting_old_tokens_when_reused() {
        val tokens = WidgetMotionTokens()
        val removed = tokens.next(7)
        val live = tokens.next(8)
        tokens.retainIds(setOf(8))
        assertFalse(tokens.isCurrent(7, removed))
        assertTrue(tokens.isCurrent(8, live))
        assertTrue(tokens.next(7) > removed)
        assertFalse(tokens.isCurrent(7, removed))
    }

    @Test fun prior_face_exists_only_in_memory_and_for_the_exact_identity() {
        val history = WidgetMotionHistory<String>()
        assertNull(history.previous(7, a))
        history.remember(7, a, "old")
        assertEquals("old", history.previous(7, a))
        assertNull(history.previous(8, a))
        assertNull(history.previous(7, a.copy(generation = 2)))
        history.retainIds(setOf(8))
        assertNull(history.previous(7, a))
        history.remember(7, a, "new")
        history.clear()
        assertNull(history.previous(7, a))
    }

    @Test fun profile_preparation_cancels_frames_before_even_a_failed_presence_read_or_clear() {
        val preparation = WidgetProfilePreparation()
        val actions = mutableListOf<String>()
        assertFalse(preparation.prepare(a, { a }, readPresence = {
            actions += "presence"
            error("launcher unavailable")
        }, clear = { error("cannot clear unknown presence") }, beforeClear = { actions += "cancel" }))
        assertEquals(listOf("cancel", "presence"), actions)
        actions.clear()
        assertTrue(preparation.prepare(a, { a }, readPresence = { WidgetPresence(true, true, false, false, false) },
            clear = { actions += "clear $it" }, beforeClear = { actions += "cancel" }))
        assertEquals(listOf("cancel", "clear Schedule", "clear Homework"), actions)
        actions.clear()
        assertTrue(preparation.prepare(a, { a }, readPresence = { WidgetPresence(true, true, false, false, false) },
            clear = { error("already prepared") }, beforeClear = { actions += "cancel" }))
        assertTrue("ordinary publication must not cancel a same-profile scene", actions.isEmpty())
    }
}
