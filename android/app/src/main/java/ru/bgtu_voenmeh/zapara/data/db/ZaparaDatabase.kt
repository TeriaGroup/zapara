package ru.bgtu_voenmeh.zapara.data.db

import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Index
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.RoomDatabase
import androidx.room.Update

// Room schema mirrors Windows Database.cs (v2: overrides/homework/strictness/alwaysShow).

@Entity(tableName = "groups")
data class GroupEntity(
    @PrimaryKey val id: String,
    val name: String,
    val url: String = ""
)

@Entity(tableName = "schedule_cache")
data class LessonEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val groupId: String,
    val dayOfWeek: Int,
    val parity: Int,
    val idx: Int,
    val timeStart: String = "",
    val timeEnd: String = "",
    val subjectRaw: String = "",
    val subjectNormalized: String = "",
    val teacherRaw: String = "",
    val roomRaw: String = "",
    val buildingRaw: String = "",
    val typeRaw: String = "",
    val classroomRaw: String = ""
)

@Entity(tableName = "friends")
data class FriendEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val groupName: String,
    val colorHex: String,
    val enabled: Boolean = true,
    val memberNames: String = ""
)

@Entity(tableName = "settings")
data class SettingsEntity(
    @PrimaryKey val id: Int = 1,
    val myGroupId: String? = null,
    val parityInvert: Boolean = false,
    val language: String = "ru",
    val periodStart: String? = null, // ISO yyyy-MM-dd
    val weekCount: Int = 2,
    val periodTitle: String? = null,
    val lastFetchedAt: String? = null,
    val intersectionStrictness: Int = 25,
    val alwaysShowAllTrafficLights: Boolean = false,
    val notifyEnabled: Boolean = true,
    val notifyTime1: String? = "20:00", // evening: tomorrow's lessons
    val notifyTime2: String? = "07:30", // morning: today's lessons
    @androidx.room.ColumnInfo(defaultValue = "'system'") val theme: String = "system",
    @androidx.room.ColumnInfo(defaultValue = "1") val animations: Boolean = true,
    @androidx.room.ColumnInfo(defaultValue = "0") val useUniversityXml: Boolean = false
)

@Entity(tableName = "overrides")
data class OverrideEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val subjectRawNormalized: String,
    val scope: String, // "global" | "weekday:N"
    val displayName: String,
    val note: String? = null,
    val createdAt: String
)

@Entity(tableName = "homework")
data class HomeworkEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val subjectRawNormalized: String,
    val text: String,
    val createdAt: String, // ISO
    val targetNthOccurrence: Int,
    val dueDateComputed: String? = null, // ISO date
    val status: String = "pending",
    val doneAt: String? = null
)

@Dao
interface GroupDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    fun upsert(group: GroupEntity)

    @Query("SELECT * FROM groups ORDER BY name")
    fun getAll(): List<GroupEntity>

    @Query("SELECT * FROM groups WHERE id = :id LIMIT 1")
    fun getById(id: String): GroupEntity?
}

@Dao
interface LessonDao {
    @Insert
    fun insertAll(lessons: List<LessonEntity>)

    @Query("DELETE FROM schedule_cache WHERE groupId = :groupId")
    fun clearForGroup(groupId: String)

    @Query(
        "SELECT * FROM schedule_cache WHERE groupId = :groupId AND dayOfWeek = :dow " +
            "AND (parity = :parity OR parity = 0) ORDER BY idx, timeStart"
    )
    fun getLessons(groupId: String, dow: Int, parity: Int): List<LessonEntity>

    @Query("SELECT * FROM schedule_cache WHERE groupId = :groupId ORDER BY dayOfWeek, parity, idx")
    fun getAllForGroup(groupId: String): List<LessonEntity>
}

@Dao
interface FriendDao {
    @Insert
    fun insert(friend: FriendEntity): Long

    @Update
    fun update(friend: FriendEntity)

    @Query("SELECT * FROM friends")
    fun getAll(): List<FriendEntity>

    @Query("DELETE FROM friends WHERE id = :id")
    fun delete(id: Long)
}

@Dao
interface SettingsDao {
    @Query("SELECT * FROM settings WHERE id = 1 LIMIT 1")
    fun observe(): kotlinx.coroutines.flow.Flow<SettingsEntity?>

    @Query("SELECT * FROM settings WHERE id = 1 LIMIT 1")
    fun get(): SettingsEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    fun save(settings: SettingsEntity)
}

@Dao
interface OverrideDao {
    @Query("SELECT * FROM overrides")
    fun getAll(): List<OverrideEntity>

    @Insert
    fun insert(e: OverrideEntity): Long

    @Query("DELETE FROM overrides WHERE subjectRawNormalized = :norm AND scope = :scope")
    fun deleteByKey(norm: String, scope: String): Int

    @Query("DELETE FROM overrides WHERE id = :id")
    fun deleteById(id: Long): Int
}

@Dao
interface HomeworkDao {
    @Query("SELECT * FROM homework ORDER BY dueDateComputed")
    fun getAll(): List<HomeworkEntity>

    @Query("SELECT * FROM homework WHERE id = :id LIMIT 1")
    fun getById(id: Long): HomeworkEntity?

    @Insert
    fun insert(e: HomeworkEntity): Long

    @Update
    fun update(e: HomeworkEntity)

    @Query("DELETE FROM homework WHERE id = :id")
    fun deleteById(id: Long): Int
}

val MIGRATION_1_2 = object : androidx.room.migration.Migration(1, 2) {
    override fun migrate(db: androidx.sqlite.db.SupportSQLiteDatabase) {
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS overrides (id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, " +
                "subjectRawNormalized TEXT NOT NULL, scope TEXT NOT NULL, displayName TEXT NOT NULL, " +
                "note TEXT, createdAt TEXT NOT NULL)"
        )
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS homework (id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, " +
                "subjectRawNormalized TEXT NOT NULL, text TEXT NOT NULL, createdAt TEXT NOT NULL, " +
                "targetNthOccurrence INTEGER NOT NULL, dueDateComputed TEXT, status TEXT NOT NULL, doneAt TEXT)"
        )
        db.execSQL("ALTER TABLE settings ADD COLUMN intersectionStrictness INTEGER NOT NULL DEFAULT 25")
        db.execSQL("ALTER TABLE settings ADD COLUMN alwaysShowAllTrafficLights INTEGER NOT NULL DEFAULT 0")
    }
}

val MIGRATION_2_3 = object : androidx.room.migration.Migration(2, 3) {
    override fun migrate(db: androidx.sqlite.db.SupportSQLiteDatabase) {
        db.execSQL("ALTER TABLE settings ADD COLUMN notifyEnabled INTEGER NOT NULL DEFAULT 1")
        db.execSQL("ALTER TABLE settings ADD COLUMN notifyTime1 TEXT DEFAULT '20:00'")
        db.execSQL("ALTER TABLE settings ADD COLUMN notifyTime2 TEXT DEFAULT '07:30'")
    }
}

val MIGRATION_3_4 = object : androidx.room.migration.Migration(3, 4) {
    override fun migrate(db: androidx.sqlite.db.SupportSQLiteDatabase) {
        db.execSQL("ALTER TABLE settings ADD COLUMN theme TEXT NOT NULL DEFAULT 'system'")
        db.execSQL("ALTER TABLE settings ADD COLUMN animations INTEGER NOT NULL DEFAULT 1")
    }
}

@Entity(tableName = "api_catalog")
data class ApiCatalogEntity(
    @PrimaryKey val groupId: String,
    val name: String
)

@Entity(tableName = "api_cache_metadata")
data class ApiCacheMetadataEntity(
    @PrimaryKey val groupId: String,
    val snapshotId: String?,
    val payload: String
)

@Dao
interface ApiCatalogDao {
    @Query("DELETE FROM api_catalog")
    fun clear()

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    fun insert(row: ApiCatalogEntity)

    @Query("SELECT * FROM api_catalog")
    fun getAll(): List<ApiCatalogEntity>
}

@Dao
interface ApiCacheMetadataDao {
    @Query("SELECT * FROM api_cache_metadata WHERE groupId = :groupId LIMIT 1")
    fun get(groupId: String): ApiCacheMetadataEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    fun upsert(row: ApiCacheMetadataEntity)

    @Query("DELETE FROM api_cache_metadata WHERE groupId = :groupId")
    fun delete(groupId: String)
}

val MIGRATION_4_5 = object : androidx.room.migration.Migration(4, 5) {
    override fun migrate(db: androidx.sqlite.db.SupportSQLiteDatabase) {
        db.execSQL("ALTER TABLE settings ADD COLUMN useUniversityXml INTEGER NOT NULL DEFAULT 0")
        db.execSQL("CREATE TABLE IF NOT EXISTS api_catalog (groupId TEXT NOT NULL, name TEXT NOT NULL, PRIMARY KEY(groupId))")
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS api_cache_metadata (groupId TEXT NOT NULL, snapshotId TEXT, payload TEXT NOT NULL, PRIMARY KEY(groupId))"
        )
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS sync_outbox (opId TEXT NOT NULL, entityType TEXT NOT NULL, entityId TEXT NOT NULL, " +
                "expectedRevision INTEGER NOT NULL, action TEXT NOT NULL, payload BLOB, localRowId INTEGER, status TEXT NOT NULL, " +
                "createdAtUtc TEXT NOT NULL, syncEpoch TEXT, PRIMARY KEY(opId))"
        )
        db.execSQL("CREATE INDEX IF NOT EXISTS idx_sync_outbox_entity ON sync_outbox(entityType, entityId, status)")
        db.execSQL(
            "CREATE TABLE IF NOT EXISTS sync_state (id INTEGER PRIMARY KEY NOT NULL, syncEpoch TEXT, afterSequence INTEGER NOT NULL)"
        )
        db.execSQL("INSERT OR IGNORE INTO sync_state (id, afterSequence) VALUES (1, 0)")
    }
}

@Entity(
    tableName = "sync_outbox",
    indices = [Index(value = ["entityType", "entityId", "status"], name = "idx_sync_outbox_entity")]
)
data class SyncOutboxEntity(
    @PrimaryKey val opId: String,
    val entityType: String,
    val entityId: String,
    val expectedRevision: Long,
    val action: String,
    val payload: ByteArray? = null,
    val localRowId: Long? = null,
    val status: String,
    val createdAtUtc: String,
    val syncEpoch: String? = null
)

@Entity(tableName = "sync_state")
data class SyncStateEntity(
    @PrimaryKey val id: Int = 1,
    val syncEpoch: String? = null,
    val afterSequence: Long = 0
)

@Database(
    entities = [
        GroupEntity::class, LessonEntity::class, FriendEntity::class, SettingsEntity::class,
        OverrideEntity::class, HomeworkEntity::class, ApiCatalogEntity::class, ApiCacheMetadataEntity::class,
        SyncOutboxEntity::class, SyncStateEntity::class
    ],
    version = 5,
    exportSchema = true
)
abstract class ZaparaDatabase : RoomDatabase() {
    abstract fun groupDao(): GroupDao
    abstract fun lessonDao(): LessonDao
    abstract fun friendDao(): FriendDao
    abstract fun settingsDao(): SettingsDao
    abstract fun overrideDao(): OverrideDao
    abstract fun homeworkDao(): HomeworkDao
    abstract fun apiCatalogDao(): ApiCatalogDao
    abstract fun apiCacheMetadataDao(): ApiCacheMetadataDao
}
