package ru.bgtu_voenmeh.zapara.data

data class GroupInfo(
    val id: String,
    val name: String,
    val url: String = ""
)

data class Lesson(
    val groupId: String = "",
    /** 1=Monday .. 6=Saturday */
    val dayOfWeek: Int = 0,
    /** 0=both, 1=odd, 2=even */
    val parity: Int = 0,
    val index: Int = 0,
    val timeStart: String = "",
    val timeEnd: String = "",
    /** full Discipline, e.g. "лек ВЫСШ. МАТЕМАТ" */
    val subjectRaw: String = "",
    val subjectNormalized: String = "",
    val teacherRaw: String = "",
    val roomRaw: String = "",
    val buildingRaw: String = "",
    val typeRaw: String = "",
    val classroomRaw: String = ""
)

data class GroupRef(val idGroup: String, val number: String)

data class LecturerInfo(val id: String, val name: String, val kafedra: String)

data class LecturerLesson(
    val lecturerId: String = "",
    val lecturerName: String = "",
    val kafedra: String = "",
    val dayOfWeek: Int = 0,
    val parity: Int = 0,
    val timeStart: String = "",
    val timeEnd: String = "",
    val disciplineRaw: String = "",
    val typeRaw: String = "",
    val subjectRaw: String = "",
    val subjectNormalized: String = "",
    val classroomRaw: String = "",
    val roomRaw: String = "",
    val buildingRaw: String = "",
    val groups: List<GroupRef> = emptyList()
)

data class Friend(
    val groupName: String,
    val colorHex: String,
    val enabled: Boolean = true,
    val memberNames: String = ""
)

data class MapInfo(
    val building: String,
    val floor: Int,
    val title: String,
    val fileName: String,
    val roomRaw: String,
    val classroomRaw: String,
    val isRemote: Boolean,
    val hasMap: Boolean,
    val note: String = ""
)

data class CoordsRect(val x: Double, val y: Double, val w: Double, val h: Double)

data class IntersectionResult(
    val friendGroupName: String,
    val friendColor: String,
    val teacher: String,
    val room: String,
    val score: Int
)
