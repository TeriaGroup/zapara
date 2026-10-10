package ru.bgtu_voenmeh.zapara.ui.maps

import ru.bgtu_voenmeh.zapara.data.CoordsRect

/**
 * #103 / AN-03: при переходе к паре на карту аудитория выделена и карта приближена к ней.
 * Только Android: на web `autoZoomNextRoom` остаётся выключенным (решение по #28).
 */
object MapFocus {
    /** Включено по умолчанию на Android; выключение возвращает прежний вид «весь этаж». */
    const val AUTO_ZOOM_LESSON_ROOM = true
    /** Комната занимает примерно эту долю плана после приближения. */
    private const val TARGET_SHARE = 0.18f
    const val MIN_ZOOM = 1.5f
    const val MAX_ZOOM = 2.5f

    data class Focus(val zoom: Float, val panX: Float, val panY: Float)

    /**
     * Масштаб и сдвиг (в долях вписанного плана, как [MapZoom.normalizedPan]), при которых центр комнаты —
     * в центре окна. Слой масштабируется от центра, поэтому сдвиг не зависит от размера окна: (0.5 − центр).
     * У краёв плана сдвиг потом ограничивает [MapZoom.restoredPan].
     */
    fun focus(rect: CoordsRect): Focus {
        val size = maxOf(rect.w, rect.h).toFloat().coerceAtLeast(0.001f)
        val zoom = (TARGET_SHARE / size).coerceIn(MIN_ZOOM, MAX_ZOOM)
        val cx = (rect.x + rect.w / 2).toFloat(); val cy = (rect.y + rect.h / 2).toFloat()
        return Focus(zoom, 0.5f - cx, 0.5f - cy)
    }

    /** Приближаем только к аудитории пары и только без выбранного шага маршрута — маршрут показывается целиком. */
    fun applies(mode: MapMode, routeStepSelected: Boolean, rect: CoordsRect?): Boolean =
        AUTO_ZOOM_LESSON_ROOM && rect != null && !routeStepSelected && (mode == MapMode.Lesson || mode == MapMode.NextLesson)
}
