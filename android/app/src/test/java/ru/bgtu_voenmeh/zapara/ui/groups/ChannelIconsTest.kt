package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Test

class ChannelIconsTest {
    private fun glyph(raw: String?, kind: String) = (ChannelIcons.resolve(raw, kind) as ChannelIconView.Glyph).glyph

    @Test fun server_icon_names_become_icons_not_text() {
        assertEquals(ChannelGlyph.MEGAPHONE, glyph("megaphone", "chat"))
        assertEquals(ChannelGlyph.BALLOT, glyph("vote", "ballots"))
        assertEquals(ChannelGlyph.BALLOT, glyph("Vote", "chat"))
    }

    @Test fun known_emoji_become_icons() {
        assertEquals(ChannelGlyph.CHAT, glyph("💬", "chat"))
        assertEquals(ChannelGlyph.BALLOT, glyph("🗳️", "ballots"))
        assertEquals(ChannelGlyph.HOMEWORK, glyph("📚", "homework"))
    }

    @Test fun unknown_names_and_empty_fall_back_to_channel_type() {
        assertEquals(ChannelGlyph.PAPERCLIP, glyph("rocket-launch", "materials"))
        assertEquals(ChannelGlyph.FILE, glyph("", "forms"))
        assertEquals(ChannelGlyph.CHAT, glyph(null, "unknown"))
    }

    @Test fun user_emoji_is_kept_as_typed() {
        assertEquals(ChannelIconView.Emoji("🦄"), ChannelIcons.resolve("🦄", "chat"))
    }

    @Test fun chat_title_never_shows_an_icon_name() {
        assertEquals("Объявления", ChannelIcons.title("megaphone", "chat", "Объявления"))
        assertEquals("Общий", ChannelIcons.title("💬", "chat", "Общий"))
        assertEquals("🦄 Флуд", ChannelIcons.title("🦄", "chat", "Флуд"))
    }
}
