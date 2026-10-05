package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class LessonFormatTest {
    @Test fun friendHint_skips_duplicate_group_and_empty_location() {
        assertEquals("Иван · 09С31 · та же аудитория", LessonFormat.friendHint("Иван", "09С31", 100, XmlCopy))
        assertEquals("А863С · в вузе", LessonFormat.friendHint("", "А863С", 25, XmlCopy))
        assertEquals("А863С", LessonFormat.friendHint("", "А863С", -1, XmlCopy))
        assertEquals("А863С", LessonFormat.friendHint("А863С", "А863С", -1, XmlCopy))
        assertEquals("Иван · А863С", LessonFormat.friendHint("Иван", "А863С", -1, XmlCopy))
    }
}
