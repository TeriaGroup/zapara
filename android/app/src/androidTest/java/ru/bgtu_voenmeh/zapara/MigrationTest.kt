package ru.bgtu_voenmeh.zapara

import androidx.room.testing.MigrationTestHelper
import androidx.sqlite.db.framework.FrameworkSQLiteOpenHelperFactory
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_3_4
import ru.bgtu_voenmeh.zapara.data.db.MIGRATION_4_5

class MigrationTest {
    @get:Rule val helper = MigrationTestHelper(
        InstrumentationRegistry.getInstrumentation(),
        ZaparaDatabase::class.java,
        emptyList(),
        FrameworkSQLiteOpenHelperFactory()
    )

    @Test fun preserves_all_v3_rows() {
        val db = helper.createDatabase("theme-migration-test", 3)
        db.execSQL("INSERT INTO groups VALUES ('3313','А863С','offline')")
        db.execSQL("INSERT INTO settings VALUES (1,'3313',1,'ru','2026-09-01',2,'Учебный год','2026-09-08',75,1,0,'21:15','08:10')")
        db.execSQL("INSERT INTO friends VALUES (7,'09С31','#FF4CC38A',1,'Иван')")
        db.execSQL("INSERT INTO overrides VALUES (8,'математика','weekday:2','Матан','Заметка','2026-09-01')")
        db.execSQL("INSERT INTO homework VALUES (9,'математика','Прочитать параграф','2026-09-01',3,'2026-09-21','done','2026-09-08')")
        db.execSQL("INSERT INTO schedule_cache VALUES (10,'3313',2,0,1,'09:00','10:35','Математика','математика','Иванов','493','ГК','лек','493;')")
        val tables = listOf("groups", "settings", "friends", "overrides", "homework", "schedule_cache")
        val before = tables.associateWith { table ->
            db.query("SELECT * FROM `$table`").use { c ->
                assertTrue(c.moveToFirst())
                (0 until c.columnCount).associate { c.getColumnName(it) to c.getString(it) }
            }
        }
        db.close()
        val migrated = helper.runMigrationsAndValidate("theme-migration-test", 5, true, MIGRATION_3_4, MIGRATION_4_5)
        before.forEach { (table, columns) ->
            migrated.query("SELECT * FROM `$table`").use { c ->
                assertEquals(1, c.count)
                assertTrue(c.moveToFirst())
                columns.forEach { (name, value) -> assertEquals("$table.$name", value, c.getString(c.getColumnIndexOrThrow(name))) }
            }
        }
        migrated.query("SELECT theme, animations, useUniversityXml FROM settings").use {
            assertTrue(it.moveToFirst())
            assertEquals("system", it.getString(0))
            assertEquals(1, it.getInt(1))
            assertEquals(0, it.getInt(2))
        }
        migrated.query("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('api_catalog','api_cache_metadata','sync_outbox','sync_state')").use {
            assertEquals(4, it.count)
        }
        migrated.close()
    }
}
