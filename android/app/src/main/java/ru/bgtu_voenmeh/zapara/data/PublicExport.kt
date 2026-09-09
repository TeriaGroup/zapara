package ru.bgtu_voenmeh.zapara.data

import android.content.ContentUris
import android.content.ContentValues
import android.content.Context
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import java.io.File

object PublicExport {
    const val FOLDER = "Военмех"
    const val SCHEDULE_FILE = "TimetableGroup50.xml"
    const val LECTURER_FILE = "TimetableLecturer50.xml"

    data class Item(val asset: String, val sub: String, val name: String, val mime: String)

    fun plan(): List<Item> {
        val maps = MapResolve.MAP_FILES.values.map { name ->
            Item("maps/$name", "maps", name, "image/jpeg")
        }
        return maps + Item("maps/coords.json", "maps", "coords.json", "application/json") +
            Item(LECTURER_FILE, "", LECTURER_FILE, "application/xml")
    }

    fun scheduleCache(ctx: Context): File = File(ctx.filesDir, SCHEDULE_FILE)

    fun ensure(ctx: Context) {
        try {
            for (item in plan()) {
                val bytes = ctx.assets.open(item.asset).use { it.readBytes() }
                write(ctx, item.sub, item.name, item.mime, bytes, skipIfExists = true)
            }
            val xml = scheduleCache(ctx)
            if (xml.exists() && xml.length() > 100) {
                write(ctx, "", SCHEDULE_FILE, "application/xml", xml.readBytes(), skipIfExists = false)
            }
        } catch (_: Exception) {
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
        if (Build.VERSION.SDK_INT >= 29) {
            writeMedia(ctx, sub, name, mime, bytes, skipIfExists)
        } else {
            writeLegacy(sub, name, bytes, skipIfExists)
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
            ctx.contentResolver.openOutputStream(existing, "wt")?.use { it.write(bytes) }
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
                if (path.trimEnd('/') + "/" == want || path.contains(FOLDER)) {
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
