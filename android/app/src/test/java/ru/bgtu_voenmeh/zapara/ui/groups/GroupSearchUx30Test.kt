package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertEquals
import org.junit.Test

class GroupSearchUx30Test {
    @Test fun wordsMatchAuthorAndAttachmentNameWithoutChangingIdentityFilters() {
        val file = GroupMessageUi("a", "Семён", "Лаба.pdf", "12:00", false, kind = "file", senderId = "a")
        val sameName = file.copy(id = "b", senderId = "b")
        assertEquals(listOf(file), browseMessages(listOf(file, sameName), "pdf семен", senderId = "a"))
        assertEquals(emptyList<GroupMessageUi>(), browseMessages(listOf(file), "физика семен"))
    }
}
