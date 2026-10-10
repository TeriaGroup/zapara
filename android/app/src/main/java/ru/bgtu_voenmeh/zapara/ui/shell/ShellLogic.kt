package ru.bgtu_voenmeh.zapara.ui.shell

import ru.bgtu_voenmeh.zapara.data.Homework
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.format.DateTimeParseException

object ShellLogic {
    /** #108 / AN-17: в открытой беседе (личной или чате группы) нижней панели нет; назад — кнопкой в шапке. */
    fun showBottomBar(current: Section, conversationOpen: Boolean): Boolean =
        !(conversationOpen && (current == Section.Chat || current == Section.Group))

    fun isStale(value: String?, now: LocalDateTime): Boolean {
        if (value == null) return true
        val parsed = try { OffsetDateTime.parse(value).toLocalDateTime() }
        catch (_: DateTimeParseException) {
            try { LocalDateTime.parse(value) } catch (_: DateTimeParseException) { return true }
        }
        return parsed.isBefore(now.minusDays(7))
    }

    // Latest approved requirement is due today/tomorrow, NOT every overdue status.
    fun homeworkBadge(items: List<Homework>, today: LocalDate): Int = items.count {
        !it.done && it.status != "done" && (it.due == today || it.due == today.plusDays(1))
    }

    /** Короткая форма чипа из каталога («нечёт.»/«чёт.»). */
    fun chipShort(groupName: String, odd: Boolean, copy: ru.bgtu_voenmeh.zapara.ui.UiCopy): String =
        copy.get("chip_group", groupName, copy.get(if (odd) "chip_odd" else "chip_even"))

    /**
     * #108 / AN-23: индекс чипа в шапке — 2 (полный «И831Б · чётная»), если заголовок, действия и чип помещаются
     * в одну строку; иначе 3 (короткий), если так помещается; если не помещается и короткий — полный (шапка
     * всё равно переносится, а полная форма читается лучше).
     */
    fun chipVariant(title: Int, actions: Int, full: Int, short: Int, gap: Int, max: Int): Int {
        val actionGap = if (actions > 0) gap else 0
        fun fits(chip: Int) = title.toLong() + gap + actions + actionGap + chip <= max
        return if (fits(full) || !fits(short)) 2 else 3
    }

    fun chip(groupName: String, odd: Boolean, copy: ru.bgtu_voenmeh.zapara.ui.UiCopy): String =
        // #108 / AN-23: чётность полностью («И831Б · чётная»), как на web и desktop.
        copy.get("chip_group", groupName, copy.get(if (odd) "week_odd" else "week_even").lowercase(java.util.Locale("ru")))
}
