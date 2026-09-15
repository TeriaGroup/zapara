package ru.bgtu_voenmeh.zapara.ui.maps

object MapsLayout {
    const val MinPlanHeight = 160
    const val ShortHeightDp = 560
    const val ChromeFraction = 0.25f
    const val StepsFraction = 0.32f
    const val SideChromeWidth = 280

    fun collapsed(widthDp: Int, heightDp: Int, fontScale: Float = 1f): Boolean =
        fontScale >= 1.5f || heightDp < ShortHeightDp || widthDp < 360

    fun compact(widthDp: Int, heightDp: Int): Boolean =
        heightDp < ShortHeightDp && widthDp > heightDp

    fun compactSteps(widthDp: Int, heightDp: Int, fontScale: Float = 1f): Boolean =
        collapsed(widthDp, heightDp, fontScale) || widthDp <= heightDp

    fun chromeMaxDp(heightDp: Int, fontScale: Float = 1f): Int {
        val room = (heightDp - MinPlanHeight).coerceAtLeast(0)
        if (fontScale >= 1.5f) return room
        return minOf(room, (heightDp * ChromeFraction).toInt())
    }
}
