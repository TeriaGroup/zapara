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
}
