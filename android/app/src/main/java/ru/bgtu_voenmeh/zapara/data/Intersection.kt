package ru.bgtu_voenmeh.zapara.data

import java.time.LocalTime

object Intersection {

    fun timesOverlap(startA: String?, endA: String?, startB: String?, endB: String?): Boolean {
        if (startA.isNullOrBlank() || startB.isNullOrBlank()) return false
        val sA = runCatching { LocalTime.parse(startA) }.getOrNull() ?: return false
        val sB = runCatching { LocalTime.parse(startB) }.getOrNull() ?: return false
        val eA = runCatching { LocalTime.parse(endA) }.getOrNull() ?: sA.plusMinutes(95)
        val eB = runCatching { LocalTime.parse(endB) }.getOrNull() ?: sB.plusMinutes(95)
        return sA < eB && sB < eA
    }

    fun floorOf(roomRaw: String?): Int {
        if (roomRaw.isNullOrBlank()) return 0
        val digits = Regex("""\d+""").find(roomRaw)?.value ?: return 0
        if (digits.isEmpty()) return 0
        val f = digits[0].toString().toIntOrNull() ?: return 0
        return if (f in 1..9) f else 0
    }

    /**
     * 100 same room / 75 same building + same floor / 50 same building /
     * 25 same time = "in uni" (buildings adjacent, NOT red) / 0 handled by caller as dimmed-off.
     */
    fun scoreOf(myRoom: String?, myBuilding: String?, frRoom: String?, frBuilding: String?): Int {
        val mine = canonBuilding(myBuilding)
        val friend = canonBuilding(frBuilding)
        val sameBuilding = mine != null && friend != null && mine.equals(friend, ignoreCase = true)
        val buildingsConflict = mine != null && friend != null && !sameBuilding
        val sameRoom = !myRoom.isNullOrBlank() && !frRoom.isNullOrBlank() &&
            myRoom.trim().equals(frRoom.trim(), ignoreCase = true) && !buildingsConflict
        if (sameRoom) return 100
        val fMy = floorOf(myRoom)
        val fFr = floorOf(frRoom)
        if (sameBuilding && fMy != 0 && fFr != 0 && fMy == fFr) return 75
        if (sameBuilding) return 50
        return 25
    }

    fun scoreToTextRu(score: Int): String = when {
        score >= 100 -> "в той же аудитории"
        score >= 75 -> "на том же этаже"
        score >= 50 -> "в том же корпусе"
        score >= 25 -> "в вузе"
        else -> "нет на месте"
    }

    /** ВЦ is the computer centre inside ГК. The same room number in УЛК and ГК is two rooms. */
    private fun canonBuilding(building: String?): String? {
        if (building.isNullOrBlank()) return null
        val t = building.trim()
        if (t.equals("ВЦ", ignoreCase = true) || t.equals("ГК", ignoreCase = true) || t.equals("main", ignoreCase = true)) return "ГК"
        if (t.equals("УЛК", ignoreCase = true)) return "УЛК"
        return t
    }

}
