package ru.bgtu_voenmeh.zapara.data

import java.io.StringReader
import java.time.LocalTime
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element
import org.xml.sax.InputSource

data class ParsedLecturerSchedule(
    val lecturers: List<LecturerInfo>,
    val lessons: List<LecturerLesson>
)

object LecturerParser {
    const val DEFAULT_URL =
        "https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableLecturer50.xml"

    private val TYPE_TOKENS = setOf("лек", "пр", "лаб", "конс", "зач", "экз", "курс", "практика")
    private val TIME_RE = Regex("""(\d{1,2}:\d{2})""")

    fun parse(xml: String): ParsedLecturerSchedule {
        val dbf = DocumentBuilderFactory.newInstance()
        dbf.isNamespaceAware = false
        try { dbf.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) } catch (_: Exception) {}
        val doc = dbf.newDocumentBuilder().parse(InputSource(StringReader(xml)))
        doc.documentElement.normalize()

        val lecturers = mutableListOf<LecturerInfo>()
        val lessons = mutableListOf<LecturerLesson>()
        val lecturerNodes = doc.getElementsByTagName("Lecturer")
        for (i in 0 until lecturerNodes.length) {
            val ln = lecturerNodes.item(i) as? Element ?: continue
            // Skip nested <Lecturer> refs inside group lessons (no IdLecturer attribute).
            if (!ln.hasAttribute("IdLecturer")) continue
            val id = ln.getAttribute("IdLecturer")
            val name = ln.getAttribute("LecturerName")
            val kaf = ln.getAttribute("Kafedra")
            if (id.isEmpty()) continue
            lecturers.add(LecturerInfo(id = id, name = name, kafedra = kaf))

            val dayNodes = ln.getElementsByTagName("Day")
            for (di in 0 until dayNodes.length) {
                val dn = dayNodes.item(di) as? Element ?: continue
                if (dn.parentNode?.parentNode != ln) continue
                var dayNum = Parity.dayTitleToNumber(dn.getAttribute("Title"))
                val lessonNodes = (dn.getElementsByTagName("LecturerLessons").item(0) as? Element)
                    ?.getElementsByTagName("Lesson") ?: continue
                for (li in 0 until lessonNodes.length) {
                    val le = lessonNodes.item(li) as? Element ?: continue
                    val timeRaw = textOf(le, "Time")
                    val parity = Parity.parseXmlParity(textOf(le, "WeekCode"), timeRaw)
                    val discRaw = textOf(le, "Discipline")
                    val classroomRaw = textOf(le, "Classroom")
                    if (dayNum == 0) dayNum = Parity.dayTitleToNumber(textOf(le, "DayTitle"))
                    if (dayNum == 0) continue

                    var typeRaw = ""
                    var subjectRaw = discRaw
                    if (discRaw.isNotBlank()) {
                        val parts = discRaw.trim().split(Regex("\\s+"), limit = 2)
                        if (parts.size == 2 && parts[0].lowercase(java.util.Locale.ROOT) in TYPE_TOKENS) {
                            typeRaw = parts[0]
                            subjectRaw = parts[1]
                        }
                    }
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
                    if (raw.isNotEmpty() && !raw.equals("дистанционно", ignoreCase = true)) {
                        val clean = raw.replace("*", "").trim()
                        val parts = clean.split(Regex("[ \t]+")).filter { it.isNotEmpty() }
                        if (parts.size >= 2 && parts[0].any { it.isLetter() }) {
                            buildingRaw = parts[0]
                            roomRaw = parts.drop(1).joinToString(" ").trimEnd(';')
                        } else {
                            roomRaw = clean
                            buildingRaw = when {
                                raw.contains("ВЦ", ignoreCase = true) -> "ВЦ"
                                raw.contains("*") -> "УЛК"
                                else -> "ГК"
                            }
                        }
                    } else if (raw.equals("дистанционно", ignoreCase = true)) {
                        roomRaw = raw
                    }

                    val groups = mutableListOf<GroupRef>()
                    val groupsEl = le.getElementsByTagName("Groups").item(0) as? Element
                    if (groupsEl != null) {
                        val gNodes = groupsEl.getElementsByTagName("Group")
                        for (g in 0 until gNodes.length) {
                            val ge = gNodes.item(g) as? Element ?: continue
                            val gid = textOf(ge, "IdGroup")
                            val gnum = textOf(ge, "Number")
                            if (gid.isNotEmpty() || gnum.isNotEmpty()) groups.add(GroupRef(gid, gnum))
                        }
                    }
                    lessons.add(
                        LecturerLesson(
                            lecturerId = id, lecturerName = name, kafedra = kaf,
                            dayOfWeek = dayNum, parity = parity,
                            timeStart = timeStart, timeEnd = timeEnd,
                            disciplineRaw = discRaw, typeRaw = typeRaw, subjectRaw = subjectRaw,
                            subjectNormalized = Parity.normalizeSubject(discRaw),
                            classroomRaw = classroomRaw, roomRaw = roomRaw, buildingRaw = buildingRaw,
                            groups = groups
                        )
                    )
                }
            }
        }
        return ParsedLecturerSchedule(lecturers, lessons)
    }

    private fun textOf(parent: Element, tag: String): String {
        val n = parent.getElementsByTagName(tag).item(0) ?: return ""
        if (n.parentNode != parent) return ""
        return n.textContent?.trim().orEmpty()
    }
}
