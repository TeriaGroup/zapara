package ru.bgtu_voenmeh.zapara.ui.week

import ru.bgtu_voenmeh.zapara.data.*
import ru.bgtu_voenmeh.zapara.data.campus.*
import java.time.LocalDate

data class RoomAgendaPlace(val id: String, val label: String)
data class RoomAgendaRow(val nodeId: String, val groupName: String, val date: LocalDate, val lesson: Lesson)
data class RoomAgenda(val date: LocalDate, val places: List<RoomAgendaPlace> = emptyList(),
    val rows: List<RoomAgendaRow> = emptyList(), val groups: List<String> = emptyList(), val unmatched: Int = 0,
    val unknown: Boolean = false)

internal fun roomAgenda(graph: CampusGraph, groups: List<Pair<GroupInfo, List<Lesson>>>,
    ctx: SchedCtx, date: LocalDate): RoomAgenda {
    val places = graph.nodes.filter { it.kind == "room" }.map { RoomAgendaPlace(it.id, "${it.building} · ${it.room}") }.sortedBy { it.label }
    if (date.isBefore(ctx.periodStart)) return RoomAgenda(date, places, unknown = true)
    val known = groups.filter { it.second.isNotEmpty() }
    var unmatched = 0
    val rows = known.flatMap { (group, lessons) ->
        Schedule.lessonsForDate(lessons, group.id, date, ctx.periodStart, ctx.weekCount, ctx.invert).mapNotNull { lesson ->
            val room = CampusRouter.resolveClassroom(graph, lesson.classroomRaw)
            if (room?.kind != "room") { unmatched++; null } else RoomAgendaRow(room.id, group.name, date, lesson)
        }
    }.sortedBy { agendaTime(it.lesson.timeStart) }
    return RoomAgenda(date, places, rows, known.map { it.first.name }.sorted(), unmatched)
}
