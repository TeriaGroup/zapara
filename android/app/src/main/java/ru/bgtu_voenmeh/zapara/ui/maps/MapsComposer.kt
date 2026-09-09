package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.CoordsRect
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.ui.LessonFormat
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.temporal.ChronoUnit

enum class MapMode { None, NextLesson, Lesson, Manual }

data class HighlightUi(val rect: CoordsRect, val label: String)

data class HighlightPx(val left: Float, val top: Float, val right: Float, val bottom: Float)

data class FittedImage(
    val originX: Float,
    val originY: Float,
    val drawnW: Float,
    val drawnH: Float
)

enum class BoxChildOrigin { TopStart, Center }

object HighlightGeometry {
    fun fit(layoutW: Float, layoutH: Float, imageW: Float, imageH: Float): FittedImage {
        if (imageW <= 0f || imageH <= 0f || layoutW <= 0f || layoutH <= 0f) {
            return FittedImage(0f, 0f, layoutW.coerceAtLeast(0f), layoutH.coerceAtLeast(0f))
        }
        val scale = minOf(layoutW / imageW, layoutH / imageH)
        val drawnW = imageW * scale
        val drawnH = imageH * scale
        return FittedImage((layoutW - drawnW) / 2f, (layoutH - drawnH) / 2f, drawnW, drawnH)
    }

    fun onLayout(rect: CoordsRect, fitted: FittedImage): HighlightPx {
        val left = fitted.originX + rect.x.toFloat() * fitted.drawnW
        val top = fitted.originY + rect.y.toFloat() * fitted.drawnH
        return HighlightPx(left, top, left + rect.w.toFloat() * fitted.drawnW, top + rect.h.toFloat() * fitted.drawnH)
    }

    fun afterLayer(px: HighlightPx, scale: Float, offsetX: Float, offsetY: Float, pivotX: Float, pivotY: Float): HighlightPx {
        fun map(x: Float, y: Float): Pair<Float, Float> =
            (pivotX + (x - pivotX) * scale + offsetX) to (pivotY + (y - pivotY) * scale + offsetY)
        val (left, top) = map(px.left, px.top)
        val (right, bottom) = map(px.right, px.bottom)
        return HighlightPx(left, top, right, bottom)
    }

    fun mapped(
        rect: CoordsRect,
        layoutW: Float,
        layoutH: Float,
        imageW: Float,
        imageH: Float,
        scale: Float,
        offsetX: Float,
        offsetY: Float
    ): HighlightPx {
        val local = onLayout(rect, fit(layoutW, layoutH, imageW, imageH))
        return afterLayer(local, scale, offsetX, offsetY, layoutW / 2f, layoutH / 2f)
    }

    /** Offset to apply so a wrap-content chip sits above [mapped] when the child origin is TopStart. */
    fun chipOffset(mapped: HighlightPx, gapPx: Float): Pair<Float, Float> =
        mapped.left to (mapped.top - gapPx)

    /**
     * Visual top-left after Compose [Modifier.offset] on a wrap-content child.
     * Center origin is the parent contentAlignment=Center placement; TopStart matches Canvas fillMaxSize.
     */
    fun offsetVisual(
        offsetX: Float,
        offsetY: Float,
        parentW: Float,
        parentH: Float,
        childW: Float,
        childH: Float,
        origin: BoxChildOrigin
    ): Pair<Float, Float> {
        val baseX = if (origin == BoxChildOrigin.Center) (parentW - childW) / 2f else 0f
        val baseY = if (origin == BoxChildOrigin.Center) (parentH - childH) / 2f else 0f
        return (baseX + offsetX) to (baseY + offsetY)
    }
}

object MapsComposer {
    fun floors(building: String): List<Int> = if (building == "УЛК") (1..5).toList() else (1..4).toList()

    fun roomText(lesson: Lesson, copy: UiCopy): String = LessonFormat.roomLabel(lesson, copy)

    fun contextLine(now: LocalDateTime, lessonsToday: List<Lesson>, next: Pair<LocalDate, Lesson>?, copy: UiCopy): String {
        val going = lessonsToday.firstOrNull { lesson ->
            val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull() ?: return@firstOrNull false
            val end = runCatching { LocalTime.parse(lesson.timeEnd) }.getOrNull() ?: return@firstOrNull false
            val t = now.toLocalTime()
            !t.isBefore(start) && t.isBefore(end)
        }
        if (going != null) return copy.get("map_ctx_now", roomText(going, copy), going.timeEnd)
        if (next == null) return copy.get("map_ctx_none")
        val (date, lesson) = next
        val room = roomText(lesson, copy)
        val today = now.toLocalDate()
        if (date == today) {
            val start = runCatching { LocalTime.parse(lesson.timeStart) }.getOrNull()
            val minutes = if (start != null) ChronoUnit.MINUTES.between(now.toLocalTime(), start).toInt().coerceAtLeast(1) else 0
            return copy.get("map_ctx_next_min", room, minutes)
        }
        if (date == today.plusDays(1)) return copy.get("map_ctx_next_tomorrow", lesson.timeStart, room)
        return copy.get("map_ctx_next_date", LessonFormat.monthDay(date), lesson.timeStart, room)
    }

    fun nextLesson(
        now: LocalDateTime,
        lessonsByDate: (LocalDate) -> List<Lesson>,
        horizonDays: Int = 14
    ): Pair<LocalDate, Lesson>? {
        val today = now.toLocalDate()
        val nowTime = now.toLocalTime()
        for (offset in 0..horizonDays) {
            val date = today.plusDays(offset.toLong())
            if (date.dayOfWeek == DayOfWeek.SUNDAY) continue
            val day = lessonsByDate(date)
            val lesson = if (offset == 0) {
                day.firstOrNull { runCatching { LocalTime.parse(it.timeStart) }.getOrNull()?.let { start -> start.isAfter(nowTime) } == true }
            } else {
                day.firstOrNull()
            }
            if (lesson != null) return date to lesson
        }
        return null
    }
}
