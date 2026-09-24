package ru.bgtu_voenmeh.zapara.groups

import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertNull
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.ui.groups.readGroupNameOffMain

@RunWith(AndroidJUnit4::class)
class GroupAccountRoomTest {
    @Test
    fun signed_in_group_reads_room_settings_without_main_thread_exception() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val db = Room.inMemoryDatabaseBuilder(context, ZaparaDatabase::class.java).build()
        try {
            val repo = ScheduleRepository(db)
            assertNull(readGroupNameOffMain { repo.settings().myGroupId })
        } finally {
            db.close()
        }
    }
}
