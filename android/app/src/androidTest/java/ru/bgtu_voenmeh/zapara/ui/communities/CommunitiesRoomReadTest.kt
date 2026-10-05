package ru.bgtu_voenmeh.zapara.ui.communities

import android.content.Context
import android.os.Looper
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Test
import org.junit.runner.RunWith
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase

@RunWith(AndroidJUnit4::class)
class CommunitiesRoomReadTest {
    @Test fun community_group_settings_read_from_main_dispatches_room_query_to_io() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val db = Room.inMemoryDatabaseBuilder(context, ZaparaDatabase::class.java).build()
        try {
            val repo = ScheduleRepository(db)
            var readOnMain = true
            val group = withContext(Dispatchers.Main) {
                readCommunityGroupIdOffMain {
                    readOnMain = Looper.myLooper() == Looper.getMainLooper()
                    repo.settings().myGroupId
                }
            }
            assertNull(group)
            assertFalse("Room settings query must leave the main thread", readOnMain)
        } finally {
            db.close()
        }
    }
}
