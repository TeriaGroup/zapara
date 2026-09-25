package ru.bgtu_voenmeh.zapara.data

import java.time.DayOfWeek
import java.time.LocalDate

object IntersectionService {

    fun intersections(
        my: Lesson,
        date: LocalDate,
        friends: List<Friend>,
        strictness: Int,
        periodStart: LocalDate,
        weekCount: Int,
        invert: Boolean,
        lessonsFor: (friendGroupId: String, dow: Int, parity: Int) -> List<Lesson>,
        resolveId: (groupName: String) -> String?
    ): List<IntersectionResult> {
        if (date.dayOfWeek == DayOfWeek.SUNDAY) return emptyList()
        val dow = date.dayOfWeek.value
        var code = Parity.weekCode(date, periodStart, weekCount)
        if (invert) code = if (code == 1) 2 else 1
        val out = mutableListOf<IntersectionResult>()
        for (f in friends.filter { it.enabled }.take(5)) {
            val fid = resolveId(f.groupName) ?: continue
            var best: IntersectionResult? = null
            for (fl in lessonsFor(fid, dow, code)) {
                if (!Intersection.timesOverlap(my.timeStart, my.timeEnd, fl.timeStart, fl.timeEnd)) continue
                val score = Intersection.scoreOf(my.roomRaw, my.buildingRaw, fl.roomRaw, fl.buildingRaw)
                if (score >= strictness && (best == null || score > best.score))
                    best = IntersectionResult(f.groupName, f.colorHex, fl.teacherRaw,
                        fl.classroomRaw.ifBlank { fl.roomRaw }, score)
            }
            best?.let(out::add)
        }
        return out
    }
}
