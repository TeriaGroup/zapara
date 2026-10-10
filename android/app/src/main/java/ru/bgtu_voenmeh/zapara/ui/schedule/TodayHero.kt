package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.R
import java.time.LocalTime

/** #108 / AN-08: закреплённый блок «Сейчас · Физика · 229 ГК · до 12:20» на «Сегодня». */
object TodayHero {
    /** Пара, которая идёт сейчас: начало ≤ now < конец. До первой пары и в перерыв — null (там «Следующая пара» в карточке). */
    fun live(lessons: List<LessonUi>, now: LocalTime): LessonUi? = lessons.firstOrNull {
        val start = runCatching { LocalTime.parse(it.timeStart) }.getOrNull()
        val end = runCatching { LocalTime.parse(it.timeEnd) }.getOrNull()
        start != null && end != null && start <= now && now < end
    }

    fun line(lesson: LessonUi, text: (Int, Array<out Any>) -> String): String {
        val room = lesson.room.takeIf { it.isNotBlank() && !lesson.remote }
        return if (room != null) text(R.string.schedule_now_hero, arrayOf(lesson.name, room, lesson.timeEnd))
        else text(R.string.schedule_now_hero_no_room, arrayOf(lesson.name, lesson.timeEnd))
    }
}
