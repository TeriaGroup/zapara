package ru.bgtu_voenmeh.zapara.ui.communities

import org.junit.Assert.assertEquals
import org.junit.Test

class CommunitiesBrowseTest {
    private val rows = listOf(
        CommunityListItemUi("one", "Н162С", "Лабораторные и проекты", "member", null, false, true),
        CommunityListItemUi("two", "А4313", "Объявления", null, null, true, false),
        CommunityListItemUi("three", "Третья группа", "Проекты", null, null, true, false)
    )

    @Test fun localSearchMatchesNameAndDescriptionWithoutChangingCatalogOrder() {
        assertEquals(listOf("one"), browseCommunities(rows, "  н162с ").map { it.communityId })
        assertEquals(listOf("one", "three"), browseCommunities(rows, "ПРОЕКТ").map { it.communityId })
        assertEquals(rows, browseCommunities(rows, ""))
        assertEquals(listOf("one", "two", "three"), rows.map { it.communityId })
    }

    @Test fun missingMatchesDoNotInventACommunity() {
        assertEquals(emptyList<CommunityListItemUi>(), browseCommunities(rows, "не существует"))
        assertEquals(emptyList<CommunityListItemUi>(), browseCommunities(emptyList(), "группа"))
    }
}
