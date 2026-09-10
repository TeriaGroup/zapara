package ru.bgtu_voenmeh.zapara.data

import android.content.ContentUris
import android.content.ContentValues
import android.content.Context
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import java.io.File

object PublicExport {
    const val FOLDER = "военмех"
    const val SCHEDULE_FILE = "TimetableGroup50.xml"
    const val LECTURER_FILE = "TimetableLecturer50.xml"

    data class Item(val asset: String, val sub: String, val name: String, val mime: String)

    fun plan(): List<Item> {
        val maps = MapResolve.MAP_FILES.values.map { name ->
            Item("maps/$name", "maps", name, "image/jpeg")
        }
        return maps + Item("maps/coords.json", "maps", "coords.json", "application/json") +
            Item(LECTURER_FILE, "", LECTURER_FILE, "application/xml") +
            Item(SCHEDULE_FILE, "", SCHEDULE_FILE, "application/xml")
    }

    fun scheduleCache(ctx: Context): File = File(ctx.filesDir, SCHEDULE_FILE)

    fun ensure(ctx: Context) {
        try {
            val cache = scheduleCache(ctx)
            if (!cache.exists() || cache.length() < 100) {
                try {
                    ctx.assets.open(SCHEDULE_FILE).use { input ->
                        cache.outputStream().use { input.copyTo(it) }
                    }
                } catch (_: Exception) {
                }
            }
            for (item in plan()) {
                val bytes = if (item.name == SCHEDULE_FILE && cache.exists() && cache.length() > 100) {
                    cache.readBytes()
                } else {
                    ctx.assets.open(item.asset).use { it.readBytes() }
                }
                write(ctx, item.sub, item.name, item.mime, bytes, skipIfExists = item.name != SCHEDULE_FILE)
            }
        } catch (e: Exception) {
            android.util.Log.w("ZaparaExport", "ensure", e)
        }
    }

    private fun write(
        ctx: Context,
        sub: String,
        name: String,
        mime: String,
        bytes: ByteArray,
        skipIfExists: Boolean
    ) {
        if (writeExisting(sub, name, bytes, skipIfExists)) return
        if (Build.VERSION.SDK_INT >= 29) {
            writeMedia(ctx, sub, name, mime, bytes, skipIfExists)
        } else {
            writeLegacy(sub, name, bytes, skipIfExists)
        }
    }

    @Suppress("DEPRECATION")
    private fun existingFolder(): File? {
        val root = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val dir = File(root, FOLDER)
        return if (dir.isDirectory) dir else null
    }

    private fun writeExisting(sub: String, name: String, bytes: ByteArray, skipIfExists: Boolean): Boolean {
        val base = existingFolder() ?: return false
        val dir = if (sub.isEmpty()) base else File(base, sub)
        if (!dir.exists() && !dir.mkdirs()) return false
        val f = File(dir, name)
        if (skipIfExists && f.exists() && f.length() > 100) return true
        return try {
            f.writeBytes(bytes)
            f.exists() && f.length() == bytes.size.toLong()
        } catch (_: Exception) {
            false
        }
    }

    @Suppress("DEPRECATION")
    private fun writeLegacy(sub: String, name: String, bytes: ByteArray, skipIfExists: Boolean) {
        val root = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val dir = if (sub.isEmpty()) File(root, FOLDER) else File(File(root, FOLDER), sub)
        dir.mkdirs()
        val f = File(dir, name)
        if (skipIfExists && f.exists() && f.length() > 100) return
        f.writeBytes(bytes)
    }

    private fun writeMedia(
        ctx: Context,
        sub: String,
        name: String,
        mime: String,
        bytes: ByteArray,
        skipIfExists: Boolean
    ) {
        val rel = if (sub.isEmpty()) {
            "${Environment.DIRECTORY_DOWNLOADS}/$FOLDER/"
        } else {
            "${Environment.DIRECTORY_DOWNLOADS}/$FOLDER/$sub/"
        }
        val existing = find(ctx, rel, name)
        if (existing != null) {
            if (skipIfExists) return
            ctx.contentResolver.openOutputStream(existing, "w")?.use { it.write(bytes) }
            return
        }
        val values = ContentValues().apply {
            put(MediaStore.Downloads.DISPLAY_NAME, name)
            put(MediaStore.Downloads.MIME_TYPE, mime)
            put(MediaStore.Downloads.RELATIVE_PATH, rel)
            put(MediaStore.Downloads.IS_PENDING, 1)
        }
        val uri = ctx.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: return
        try {
            ctx.contentResolver.openOutputStream(uri)?.use { it.write(bytes) }
            values.clear()
            values.put(MediaStore.Downloads.IS_PENDING, 0)
            ctx.contentResolver.update(uri, values, null, null)
        } catch (e: Exception) {
            ctx.contentResolver.delete(uri, null, null)
            throw e
        }
    }

    private fun find(ctx: Context, rel: String, name: String): android.net.Uri? {
        if (Build.VERSION.SDK_INT < 29) return null
        val want = rel.trimEnd('/') + "/"
        ctx.contentResolver.query(
            MediaStore.Downloads.EXTERNAL_CONTENT_URI,
            arrayOf(MediaStore.Downloads._ID, MediaStore.Downloads.RELATIVE_PATH),
            "${MediaStore.Downloads.DISPLAY_NAME}=?",
            arrayOf(name),
            null
        )?.use { c ->
            val idCol = c.getColumnIndexOrThrow(MediaStore.Downloads._ID)
            val pathCol = c.getColumnIndexOrThrow(MediaStore.Downloads.RELATIVE_PATH)
            while (c.moveToNext()) {
                val path = (c.getString(pathCol) ?: "").replace('\\', '/')
                val norm = path.trimEnd('/') + "/"
                if (norm.equals(want, ignoreCase = true) || path.contains(FOLDER, ignoreCase = true)) {
                    return ContentUris.withAppendedId(
                        MediaStore.Downloads.EXTERNAL_CONTENT_URI,
                        c.getLong(idCol)
                    )
                }
            }
        }
        return null
    }
}
