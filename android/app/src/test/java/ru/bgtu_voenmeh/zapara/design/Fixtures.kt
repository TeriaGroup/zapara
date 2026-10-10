package ru.bgtu_voenmeh.zapara.design

import android.content.Context
import ru.bgtu_voenmeh.zapara.data.Homework
import ru.bgtu_voenmeh.zapara.data.Lesson
import ru.bgtu_voenmeh.zapara.data.Parity
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.schedule.DayPage
import ru.bgtu_voenmeh.zapara.ui.schedule.FriendDotUi
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleComposer
import java.time.LocalDate
import java.time.LocalDateTime

object Fx {
    const val GROUP = "И831Б"
    val today: LocalDate = LocalDate.of(2026, 10, 8) // Thursday
    val now: LocalDateTime = today.atTime(11, 5)
    val periodStart: LocalDate = LocalDate.of(2026, 9, 1)
    val ctx = SchedCtx(GROUP, periodStart, 2, false)

    private fun l(dow: Int, idx: Int, s: String, e: String, type: String, subj: String, teacher: String, room: String, parity: Int = 0) =
        Lesson(GROUP, dow, parity, idx, s, e, "$type $subj", Parity.normalizeSubject("$type $subj"), teacher, room, "", type, "$room;")

    val lessons: List<Lesson> = listOf(
        // Monday
        l(1, 1, "09:00", "10:30", "лек", "Математический анализ", "Соколова Е. В.", "322"),
        l(1, 2, "10:50", "12:20", "пр", "Математический анализ", "Соколова Е. В.", "418"),
        l(1, 3, "12:40", "14:10", "лаб", "Программирование на C++", "Иванов А. С.", "ВЦ-3"),
        // Tuesday
        l(2, 2, "10:50", "12:20", "лек", "Физика", "Петров Н. Н.", "221"),
        l(2, 3, "12:40", "14:10", "пр", "Иностранный язык", "Смирнова О. А.", "507"),
        // Wednesday
        l(3, 1, "09:00", "10:30", "лек", "Теоретическая механика", "Кузнецов В. П.", "*112"),
        l(3, 2, "10:50", "12:20", "пр", "Теоретическая механика", "Кузнецов В. П.", "*204"),
        // Thursday (today): 4 pairs with a long break
        l(4, 1, "09:00", "10:30", "лек", "Математический анализ", "Соколова Е. В.", "322"),
        l(4, 2, "10:50", "12:20", "пр", "Физика", "Петров Н. Н.", "229"),
        l(4, 3, "12:40", "14:10", "лаб", "Программирование на C++", "Иванов А. С.", "ВЦ-3"),
        l(4, 4, "14:55", "16:25", "пр", "Иностранный язык", "Смирнова О. А.", "507"),
        // Friday
        l(5, 1, "09:00", "10:30", "лек", "История России", "Морозова Т. Г.", "дистанционно"),
        l(5, 2, "10:50", "12:20", "пр", "Инженерная графика", "Волков Д. И.", "*310"),
        // Saturday
        l(6, 2, "10:50", "12:20", "пр", "Физическая культура", "—", "Спортзал")
    )

    fun homework(): List<Homework> = listOf(
        Homework(1, Parity.normalizeSubject("пр Физика"), "Задачи 3.14–3.20 из Иродова, оформить в тетради", today.minusDays(7), 1, today, "burning", false),
        Homework(2, Parity.normalizeSubject("лаб Программирование на C++"), "Лабораторная №3: шаблоны и STL, отчёт в PDF", today.minusDays(5), 1, today.plusDays(7), "pending", false),
        Homework(3, Parity.normalizeSubject("пр Иностранный язык"), "Text 5 — перевод и пересказ, слова к диктанту", today.minusDays(7), 1, today, "done", true),
        Homework(4, Parity.normalizeSubject("пр Математический анализ"), "Типовой расчёт №2, варианты 7 и 12", today.minusDays(3), 1, today.plusDays(4), "pending", false)
    )

    fun page(context: Context, date: LocalDate, withFriends: Boolean = true): DayPage {
        val copy = AndroidUiCopy(context)
        val hw = homework()
        return ScheduleComposer.page(date, lessons, ctx, now,
            displayName = { _, _ -> "" },
            homeworkFor = { norm -> hw.filter { it.norm == norm && (it.due == null || !it.due!!.isBefore(date)) } },
            friendsFor = { lesson ->
                if (!withFriends || lesson.index != 2) emptyList() else listOf(
                    FriendDotUi(0, "И832Б", "Аня, Миша", 100, "Аня, Миша · И832Б · в той же аудитории"),
                    FriendDotUi(1, "О711Б", "Дима", 60, "Дима · О711Б · рядом"))
            }, copy = copy).let { p ->
                p.copy(deadlines = hw.filter { it.due != null && !it.due!!.isBefore(date) && !it.due!!.isAfter(date.plusDays(2)) }
                    .map { ru.bgtu_voenmeh.zapara.ui.schedule.HomeworkRowUi(it.id, it.text,
                        ru.bgtu_voenmeh.zapara.ui.LessonFormat.hwCardLabel(it, copy), it.status, it.done) })
            }
    }

    fun pages(context: Context, from: LocalDate = today.minusDays(3), days: Int = 10) =
        (0 until days).associate { from.plusDays(it.toLong()).let { d -> d to page(context, d) } }
}
