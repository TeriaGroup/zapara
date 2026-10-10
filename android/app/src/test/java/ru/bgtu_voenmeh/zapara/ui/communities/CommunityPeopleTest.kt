package ru.bgtu_voenmeh.zapara.ui.communities

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.assertNotEquals
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.communities.Classmate
import java.io.File

/** #104 (AN-24): участники, персонал и заявки — по имени; userId в интерфейсе не виден. */
class CommunityPeopleTest {
    private fun mate(login: String, display: String?) = Classmate("u", login, display, "member", false)

    @Test fun display_name_first_login_second_only_when_different() {
        assertEquals(CommunityPeople.Label("Иванов Иван", "ivanov"), CommunityPeople.label(mate("ivanov", "Иванов Иван")))
        assertEquals(CommunityPeople.Label("ivanov", null), CommunityPeople.label(mate("ivanov", "ivanov")))
        assertEquals(CommunityPeople.Label("Ivanov", null), CommunityPeople.label(mate("ivanov", "Ivanov")))
    }

    @Test fun login_when_no_name_and_neutral_when_nothing() {
        assertEquals(CommunityPeople.Label("petrova", null), CommunityPeople.label(mate("petrova", null)))
        assertEquals(CommunityPeople.Label("petrova", null), CommunityPeople.label(mate("petrova", "  ")))
        assertEquals(CommunityPeople.Label(null, null), CommunityPeople.label(null))
    }

    @Test fun section_never_prints_user_id() {
        val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/communities/CommunitiesSection.kt").readText()
        assertFalse(Regex("Text\\([^)]*userId").containsMatchIn(section))
        assertTrue(section.contains("val shown = name ?: stringResource(R.string.community_person_unnamed, CommunityPeople.stableNumber(userId))"))
        assertTrue(File("src/main/res/values/strings_community_people.xml").readText().contains(">Участник №%1\$d<"))
    }

    @Test fun unnamed_number_is_stable_per_person_across_lists_and_reloads() {
        val a = "8f6c2f0e-1111-4c1a-9e0b-0000000000a1"
        val b = "8f6c2f0e-1111-4c1a-9e0b-0000000000b2"
        assertEquals(CommunityPeople.stableNumber(a), CommunityPeople.stableNumber(a))
        assertEquals("регистр и пробелы не меняют номер", CommunityPeople.stableNumber(a), CommunityPeople.stableNumber(" ${a.uppercase()} "))
        assertNotEquals(CommunityPeople.stableNumber(a), CommunityPeople.stableNumber(b))
        assertTrue(CommunityPeople.stableNumber(a) in 1000..9999)
        // Одна функция для всех трёх списков — номер не зависит от позиции.
        val section = File("src/main/java/ru/bgtu_voenmeh/zapara/ui/communities/CommunitiesSection.kt").readText()
        assertEquals(3, Regex("CommunityPersonRow\\([a-z]+\\.name, [a-z]+\\.login, [a-z]+\\.userId,").findAll(section).count())
        assertFalse(section.contains("indexOf(request)"))
    }

    @Test fun numbers_rarely_collide() {
        val ids = (0 until 200).map { java.util.UUID.nameUUIDFromBytes("user-$it".toByteArray()).toString() }
        assertTrue(ids.map(CommunityPeople::stableNumber).toSet().size >= 195)
    }
}
