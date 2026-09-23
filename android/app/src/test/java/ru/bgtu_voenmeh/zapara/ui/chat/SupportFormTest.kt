package ru.bgtu_voenmeh.zapara.ui.chat

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SupportFormTest {
    @Test
    fun guestDoesNotOpenAThreadAndASignedInExchangeStaysOneThread() {
        val guest = SupportForm.submit(false, emptyList(), "Кнопка", "Не нажимается кнопка пары")
        assertEquals("Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.", guest.error)
        assertEquals(0, guest.thread.size)
        val opened = SupportForm.submit(true, emptyList(), "Кнопка", "Не нажимается кнопка пары")
        assertNull(opened.error)
        val replied = SupportForm.reply(opened.thread, "Поправили переключатель.")
        val followed = SupportForm.submit(true, replied, "уточнение", "Теперь нажимается.")
        assertEquals(listOf("user", "operator", "user"), followed.thread.map { it.author })
        assertEquals("Не нажимается кнопка пары", followed.thread[0].body)
        assertEquals("Теперь нажимается.", followed.thread[2].body)
    }
}
