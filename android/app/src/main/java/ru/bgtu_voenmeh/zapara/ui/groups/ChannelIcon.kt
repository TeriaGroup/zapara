package ru.bgtu_voenmeh.zapara.ui.groups

import androidx.annotation.DrawableRes
import ru.bgtu_voenmeh.zapara.R

/**
 * Значок канала (#30, как web PR #52): сервер присылает либо эмодзи (так создаёт web),
 * либо имя (стартовые каналы: "megaphone", "vote"). Имя и известное эмодзи → иконка;
 * неизвестное имя → иконка по типу канала; прочие эмодзи пользователя показываются как есть.
 */
enum class ChannelGlyph(@DrawableRes val drawable: Int) {
    CHAT(R.drawable.ic_chat), PIN(R.drawable.ic_pin), MEGAPHONE(R.drawable.ic_megaphone),
    BALLOT(R.drawable.ic_ballot), HOMEWORK(R.drawable.ic_homework), CALENDAR(R.drawable.ic_calendar),
    PAPERCLIP(R.drawable.ic_paperclip), FILE(R.drawable.ic_file),
}

sealed interface ChannelIconView {
    data class Glyph(val glyph: ChannelGlyph) : ChannelIconView
    data class Emoji(val text: String) : ChannelIconView
}

object ChannelIcons {
    private val named: Map<String, ChannelGlyph> = mapOf(
        "chat" to ChannelGlyph.CHAT, "message" to ChannelGlyph.CHAT, "💬" to ChannelGlyph.CHAT,
        "pin" to ChannelGlyph.PIN, "📌" to ChannelGlyph.PIN,
        "megaphone" to ChannelGlyph.MEGAPHONE, "announcement" to ChannelGlyph.MEGAPHONE, "announcements" to ChannelGlyph.MEGAPHONE,
        "📢" to ChannelGlyph.MEGAPHONE, "📣" to ChannelGlyph.MEGAPHONE,
        "vote" to ChannelGlyph.BALLOT, "ballot" to ChannelGlyph.BALLOT, "ballots" to ChannelGlyph.BALLOT,
        "poll" to ChannelGlyph.BALLOT, "polls" to ChannelGlyph.BALLOT, "🗳️" to ChannelGlyph.BALLOT, "🗳" to ChannelGlyph.BALLOT,
        "homework" to ChannelGlyph.HOMEWORK, "book" to ChannelGlyph.HOMEWORK, "📚" to ChannelGlyph.HOMEWORK, "📒" to ChannelGlyph.HOMEWORK,
        "calendar" to ChannelGlyph.CALENDAR, "schedule" to ChannelGlyph.CALENDAR, "📅" to ChannelGlyph.CALENDAR,
        "paperclip" to ChannelGlyph.PAPERCLIP, "materials" to ChannelGlyph.PAPERCLIP, "📎" to ChannelGlyph.PAPERCLIP,
        "file" to ChannelGlyph.FILE, "forms" to ChannelGlyph.FILE, "form" to ChannelGlyph.FILE,
    )
    private val byKind: Map<String, ChannelGlyph> = mapOf(
        "chat" to ChannelGlyph.CHAT, "ballots" to ChannelGlyph.BALLOT, "forms" to ChannelGlyph.FILE,
        "materials" to ChannelGlyph.PAPERCLIP, "homework" to ChannelGlyph.HOMEWORK, "schedule" to ChannelGlyph.CALENDAR,
    )
    private val nameLike = Regex("^[a-z0-9-]+$", RegexOption.IGNORE_CASE)

    fun resolve(raw: String?, kind: String?): ChannelIconView {
        val value = raw.orEmpty().trim()
        val known = named[value] ?: named[value.lowercase()]
        if (known != null) return ChannelIconView.Glyph(known)
        if (value.isEmpty() || nameLike.matches(value)) return ChannelIconView.Glyph(byKind[kind] ?: ChannelGlyph.CHAT)
        return ChannelIconView.Emoji(value)
    }

    /** Заголовок открытого канала: эмодзи пользователя перед названием, имя значка («megaphone») — никогда. */
    fun title(raw: String?, kind: String?, title: String): String =
        when (val view = resolve(raw, kind)) {
            is ChannelIconView.Emoji -> "${view.text} $title"
            is ChannelIconView.Glyph -> title
        }
}
