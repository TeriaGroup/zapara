package ru.bgtu_voenmeh.zapara.ui

data class StudyInterval(val start: String, val end: String)
data class FreeStudyInterval(val start: String, val end: String, val minutes: Int)
data class TransferAssessment(val status: String, val availableSeconds: Int?, val routeSeconds: Int?)

object StudyPlanning {
    private fun minutes(value: String): Int? {
        val match = Regex("^(\\d{1,2}):(\\d{2})$").matchEntire(value) ?: return null
        val hour = match.groupValues[1].toInt(); val minute = match.groupValues[2].toInt()
        return if (hour < 24 && minute < 60) hour * 60 + minute else null
    }
    private fun time(value: Int) = "%02d:%02d".format(java.util.Locale.ROOT, value / 60, value % 60)
    fun commonFreeIntervals(first: List<StudyInterval>, second: List<StudyInterval>, minimumMinutes: Int = 15): List<FreeStudyInterval> {
        if (first.isEmpty() || second.isEmpty() || minimumMinutes < 1) return emptyList()
        val converted = (first + second).map { row ->
            val start = minutes(row.start) ?: return emptyList()
            val end = minutes(row.end) ?: return emptyList()
            if (end <= start) return emptyList()
            start to end
        }
        val a = converted.take(first.size); val b = converted.drop(first.size)
        val low = maxOf(a.minOf { it.first }, b.minOf { it.first })
        val high = minOf(a.maxOf { it.second }, b.maxOf { it.second })
        if (high <= low) return emptyList()
        val result = mutableListOf<FreeStudyInterval>(); var cursor = low
        for ((from, to) in converted.sortedWith(compareBy<Pair<Int, Int>> { it.first }.thenBy { it.second })) {
            if (to <= low || from >= high) continue
            val start = maxOf(low, from); val end = minOf(high, to)
            if (start - cursor >= minimumMinutes) result += FreeStudyInterval(time(cursor), time(start), start - cursor)
            cursor = maxOf(cursor, end)
        }
        if (high - cursor >= minimumMinutes) result += FreeStudyInterval(time(cursor), time(high), high - cursor)
        return result
    }
    fun assessTransfer(previousEnd: String, nextStart: String, routeSeconds: Int?): TransferAssessment {
        val end = minutes(previousEnd); val start = minutes(nextStart); val route = routeSeconds?.takeIf { it >= 0 }
        if (end == null || start == null) return TransferAssessment("unknown", null, route)
        val available = (start - end) * 60
        return TransferAssessment(when { available < 0 -> "overlap"; route == null -> "unknown"; route > available -> "tight"; else -> "fits" }, available, route)
    }
}
