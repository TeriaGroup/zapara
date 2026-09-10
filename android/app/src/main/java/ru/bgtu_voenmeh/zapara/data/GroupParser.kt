package ru.bgtu_voenmeh.zapara.data

import java.io.InputStream
import java.io.InputStreamReader
import java.io.StringReader
import java.nio.charset.StandardCharsets
import java.time.LocalDate
import java.time.LocalTime
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element
import org.xml.sax.InputSource

// Port of Vograph.Core ParserService (student XML). DOM-based so it runs
// in JVM unit tests and on device (same approach as Windows XmlDocument).
data class ParsedSchedule(
    val groups: List<GroupInfo>,
    val lessons: List<Lesson>,
    val periodStart: LocalDate,
    val weekCount: Int,
    val periodTitle: String
)

object GroupParser {
    const val DEFAULT_URL =
        "https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableGroup50.xml"

    private val TYPE_TOKENS = setOf("лек", "пр", "лаб", "конс", "зач", "экз", "курс", "практика")
    private val TIME_RE = Regex("""(\d{1,2}:\d{2})""")
    private val DIGITS_RE = Regex("""\d+""")

    fun parse(xml: String, url: String = DEFAULT_URL): ParsedSchedule =
        parse(InputSource(StringReader(xml)), url)

    fun parse(stream: InputStream, url: String = DEFAULT_URL): ParsedSchedule =
        parse(InputSource(InputStreamReader(stream, StandardCharsets.UTF_8)), url)

    private fun parse(source: InputSource, url: String): ParsedSchedule {
        val dbf = DocumentBuilderFactory.newInstance()
        dbf.isNamespaceAware = false
        try { dbf.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) } catch (_: Exception) {}
        val doc = dbf.newDocumentBuilder().parse(source)
        doc.documentElement.normalize()

        val periodEl = doc.getElementsByTagName("Period").item(0) as? Element
            ?: throw IllegalStateException("нет Period")
        val title = periodEl.getAttribute("Title")
        val sy = periodEl.getAttribute("StartYear").toIntOrNull() ?: 2026
        val sm = periodEl.getAttribute("StartMonth").toIntOrNull() ?: 9
        val sd = periodEl.getAttribute("StartDay").toIntOrNull() ?: 1
        val periodStart = LocalDate.of(sy, sm, sd)
        val weeksEl = doc.getElementsByTagName("Weeks").item(0) as? Element
        val weekCount = weeksEl?.getAttribute("WeekCount")?.toIntOrNull() ?: 2

        val groups = mutableListOf<GroupInfo>()
        val lessons = mutableListOf<Lesson>()
        val groupNodes = doc.getElementsByTagName("Group")
        for (gi in 0 until groupNodes.length) {
            val gn = groupNodes.item(gi) as? Element ?: continue
            // Skip lecturer-view <Group> refs (IdGroup/Number children instead of attributes).
            if (!gn.hasAttribute("IdGroup")) continue
            val id = gn.getAttribute("IdGroup")
            val name = gn.getAttribute("Number")
            if (id.isEmpty()) continue
            groups.add(GroupInfo(id = id, name = name, url = url))

            val dayNodes = gn.getElementsByTagName("Day")
            for (di in 0 until dayNodes.length) {
                val dn = dayNodes.item(di) as? Element ?: continue
                // Only days directly under this group's Days node.
                if (dn.parentNode?.parentNode != gn) continue
                val dayNum = Parity.dayTitleToNumber(dn.getAttribute("Title"))
                if (dayNum == 0) continue
                val lessonNodes = (dn.getElementsByTagName("GroupLessons").item(0) as? Element)
                    ?.getElementsByTagName("Lesson") ?: continue
                val indexPerParity = mutableMapOf<Int, Int>()
                for (li in 0 until lessonNodes.length) {
                    val ln = lessonNodes.item(li) as? Element ?: continue
                    val parity = textOf(ln, "WeekCode").toIntOrNull() ?: 0
                    val timeRaw = textOf(ln, "Time")
                    val discRaw = textOf(ln, "Discipline")
                    val classroomRaw = textOf(ln, "Classroom")

                    val teachers = mutableListOf<String>()
                    val lecturersEl = ln.getElementsByTagName("Lecturers").item(0) as? Element
                    if (lecturersEl != null) {
                        val sn = lecturersEl.getElementsByTagName("ShortName")
                        for (k in 0 until sn.length) {
                            val t = sn.item(k).textContent?.trim().orEmpty()
                            if (t.isNotEmpty()) teachers.add(t)
                        }
                    }

                    var typeRaw = ""
                    var subjectOnly = discRaw
                    if (discRaw.isNotBlank()) {
                        val parts = discRaw.trim().split(Regex("\\s+"), limit = 2)
                        if (parts.size == 2 && parts[0].lowercase() in TYPE_TOKENS) {
                            typeRaw = parts[0]
                            subjectOnly = parts[1]
                        }
                    }
                    @Suppress("UNUSED_VARIABLE")
                    val ignoredSubjectOnly = subjectOnly

                    var timeStart = ""
                    var timeEnd = ""
                    val tm = TIME_RE.find(timeRaw)
                    if (tm != null) {
                        timeStart = tm.groupValues[1].padStart(5, '0')
                        try {
                            val te = LocalTime.parse(timeStart).plusMinutes(95)
                            timeEnd = "%02d:%02d".format(te.hour, te.minute)
                        } catch (_: Exception) {}
                    }

                    var roomRaw = ""
                    var buildingRaw = ""
                    val raw = classroomRaw.trim().trimEnd(';').trim()
                    if (raw.isNotEmpty()) {
                        if (raw.equals("дистанционно", ignoreCase = true)) {
                            roomRaw = raw
                        } else {
                            val clean = raw.replace("*", "").trim()
                            val parts = clean.split(Regex("[ \t]+")).filter { it.isNotEmpty() }
                            if (parts.size >= 2 && parts[0].any { it.isLetter() }) {
                                buildingRaw = parts[0]
                                roomRaw = parts.drop(1).joinToString(" ").trimEnd(';')
                            } else {
                                roomRaw = clean
                                buildingRaw = when {
                                    raw.contains("ВЦ", ignoreCase = true) -> "ВЦ"
                                    raw.contains("*") -> "УЛК" // star = УЛК (user correction 2026-09-01)
                                    else -> "ГК"
                                }
                            }
                            if (buildingRaw == "main") buildingRaw = "ГК"
                        }
                    }

                    val idx = (indexPerParity[parity] ?: 0) + 1
                    indexPerParity[parity] = idx
                    lessons.add(
                        Lesson(
                            groupId = id, dayOfWeek = dayNum, parity = parity, index = idx,
                            timeStart = timeStart, timeEnd = timeEnd,
                            subjectRaw = discRaw,
                            subjectNormalized = Parity.normalizeSubject(discRaw),
                            teacherRaw = teachers.joinToString("; "),
                            roomRaw = roomRaw, buildingRaw = buildingRaw,
                            typeRaw = typeRaw, classroomRaw = classroomRaw
                        )
                    )
                }
            }
        }
        return ParsedSchedule(groups, lessons, periodStart, weekCount, title)
    }

    private fun textOf(parent: Element, tag: String): String {
        val n = parent.getElementsByTagName(tag).item(0) ?: return ""
        // Direct child only (avoid nested matches).
        if (n.parentNode != parent) return ""
        return n.textContent?.trim().orEmpty()
    }
}
