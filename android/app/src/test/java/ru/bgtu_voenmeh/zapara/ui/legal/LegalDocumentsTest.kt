package ru.bgtu_voenmeh.zapara.ui.legal

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class LegalDocumentsTest {
    @Test fun both_documents_are_the_texts_the_account_entry_shows() {
        val legal = File("src/main").resolve("../../../../legal").canonicalFile
        fun open(name: String) = File(legal, name).inputStream()
        val agreement = LegalDocuments.open(::open, "agreement")
        val policy = LegalDocuments.open(::open, "policy")
        assertEquals("Пользовательское соглашение", agreement.title)
        assertEquals("Политика обработки персональных данных", policy.title)
        for (doc in listOf(agreement, policy)) {
            assertTrue(doc.body.contains("Расписание военмех"))
            assertTrue(doc.body.contains("неофициальное"))
            assertTrue(doc.body.contains("не является сервисом университета"))
            assertTrue(doc.body.contains("расписание, карты и локальные записи остаются на устройстве"))
            assertTrue(doc.body.contains("Аккаунт необязателен"))
            assertTrue(doc.body.contains("на сервере оператора в России хранятся только данные, которые нужны этому аккаунту и выбранной синхронизации"))
            assertTrue(doc.body.contains("Рекламных SDK нет"))
            assertTrue(doc.body.contains("Сторонней аналитики нет"))
            assertTrue(doc.body.contains("https://github.com/TeriaGroup/zapara"))
            assertFalse(doc.body.contains("ИНН"))
            assertFalse(doc.body.contains("почтовый адрес"))
        }
    }
}
