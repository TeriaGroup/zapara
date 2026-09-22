package ru.bgtu_voenmeh.zapara.data

// Building/floor lookup only. This file does not load coords.json.

object MapResolve {
    val MAP_FILES: Map<Pair<String, Int>, String> = mapOf(
        ("ГК" to 1) to "karta-glavnyj-korpus-1-etazh-2022.jpg",
        ("ГК" to 2) to "karta-glavnyj-korpus-2-etazh-2022.jpg",
        ("ГК" to 3) to "karta-glavnyj-korpus-3-etazh-2022.jpg",
        ("ГК" to 4) to "karta-glavnyj-korpus-4-etazh-2022.jpg",
        ("УЛК" to 1) to "karta-ulk.-1-etazh-2022.jpg",
        ("УЛК" to 2) to "karta-ulk.-2-etazh-2022.jpg",
        ("УЛК" to 3) to "karta-ulk.-3-etazh-2022.jpg",
        ("УЛК" to 4) to "karta-ulk.-4-etazh-2022.jpg",
        ("УЛК" to 5) to "karta-ulk.-5-etazh-2022.jpg"
    )

    private val VC_RE = Regex("""ВЦ\s*(.+)""", RegexOption.IGNORE_CASE)
    private val DIGITS_RE = Regex("""\d+""")

    fun resolve(classroomRaw: String?): MapInfo? {
        if (classroomRaw.isNullOrBlank()) return null
        val raw = classroomRaw.trim().trimEnd(';').trim()
        if (raw.contains("дистанционно", ignoreCase = true)) {
            return MapInfo(
                building = "дистанционно", floor = 0, title = "Дистанционно",
                fileName = "", roomRaw = raw, classroomRaw = classroomRaw,
                isRemote = true, hasMap = false, note = "Занятие дистанционно — карта не требуется"
            )
        }
        val hasStar = raw.contains("*")
        val building: String
        var roomPart = raw
        if (raw.contains("ВЦ", ignoreCase = true)) {
            building = "ВЦ"
            roomPart = VC_RE.find(raw)?.groupValues?.get(1)?.trim()?.ifBlank { null }
                ?: raw.replace(Regex("""^ВЦ\s*""", RegexOption.IGNORE_CASE), "").trim().ifBlank { raw }
        } else if (hasStar) {
            building = "УЛК" // star = УЛК (user correction 2026-09-01)
            roomPart = raw.replace("*", "").trim()
        } else {
            building = "ГК"
            roomPart = raw
        }

        var floor = if (roomPart.firstOrNull()?.isDigit() == true)
            DIGITS_RE.find(roomPart)?.value?.firstOrNull()?.toString()?.toIntOrNull() ?: 1
        else 1
        if (floor < 1) floor = 1
        if (floor > 5) floor = 5
        val mapBuilding = if (building == "ВЦ") "ГК" else building
        if (mapBuilding == "ГК" && floor > 4) floor = 4

        var title: String
        var fileName = ""
        var hasMap = false
        var note = ""
        if (building == "ВЦ") {
            title = "ВЦ · $mapBuilding $floor этаж · ауд. $roomPart"
            MAP_FILES[mapBuilding to floor]?.let {
                fileName = it; hasMap = true; note = "ВЦ — показать план ГК"
            }
        } else {
            title = "$building · $floor этаж · ауд. ${raw.replace(";", "").trim()}"
            val direct = MAP_FILES[building to floor]
            if (direct != null) {
                fileName = direct; hasMap = true
            } else {
                MAP_FILES["ГК" to minOf(floor, 4)]?.let {
                    fileName = it; hasMap = true
                    note = "Карта для $building $floor этажа — показан ближайший план"
                }
            }
        }
        return MapInfo(
            building = building, floor = floor, title = title, fileName = fileName,
            roomRaw = roomPart, classroomRaw = classroomRaw,
            isRemote = false, hasMap = hasMap, note = note
        )
    }

    /** Normalized room key for coords.json lookup. */
    fun roomKey(roomRaw: String?): String =
        roomRaw?.trim()?.trimEnd(';')?.replace("*", "")?.trim()?.lowercase().orEmpty()

    fun findCoords(
        coords: Map<String, Map<String, CoordsRect>>,
        building: String,
        floor: Int,
        roomRaw: String?
    ): CoordsRect? {
        val inner = coords[building + " " + floor] ?: return null
        val key = roomKey(roomRaw)
        inner[key]?.let { return it }
        val digits = DIGITS_RE.find(key)?.value ?: return null
        return inner[digits]
    }
}
