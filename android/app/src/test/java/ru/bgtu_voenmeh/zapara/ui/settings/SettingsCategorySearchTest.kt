package ru.bgtu_voenmeh.zapara.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SettingsCategorySearchTest {
    private val terms = linkedMapOf(
        "account" to "аккаунт профиль вход пароль",
        "study" to "учёба группа подгруппа расписание",
        "appearance" to "вид тема",
        "notifications" to "уведомления напоминания",
        "maps" to "карты маршруты",
        "data" to "данные синхронизация",
        "updates" to "обновления версия",
        "help" to "помощь поддержка"
    )
    @Test fun russian_aliases_resolve_exact_existing_category_and_clear_restores_all() {
        assertEquals(setOf("notifications"), SettingsCategorySearch.visible("напоминания", terms))
        assertEquals(setOf("study"), SettingsCategorySearch.visible("подгруппа", terms))
        assertEquals(setOf("account"), SettingsCategorySearch.visible("пароль", terms))
        assertTrue(SettingsCategorySearch.visible("несуществующий раздел", terms).isEmpty())
        assertEquals(8, SettingsCategorySearch.visible("", terms).size)
    }
}
