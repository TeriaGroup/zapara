package ru.bgtu_voenmeh.zapara

import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository

class ThemePreferencesTest {
    @Test fun preference_round_trip_preserves_other_settings() = runBlocking {
        val repo = ScheduleRepository.get(InstrumentationRegistry.getInstrumentation().targetContext)
        withContext(Dispatchers.IO) {
            val before = repo.settings()
            try {
                repo.saveSettings(before.copy(theme = "light", animations = false))
                assertEquals(before.copy(theme = "light", animations = false), repo.settings())
                val observed = repo.db.settingsDao().observe().first()
                assertNotNull(observed)
                assertEquals("light", observed!!.theme)
                assertFalse(observed.animations)
            } finally {
                repo.saveSettings(before)
            }
        }
    }
}
