package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.provider.OpenableColumns
import java.io.ByteArrayOutputStream
import java.io.File
import java.security.MessageDigest
import java.util.UUID

data class HomeworkStoredFile(
    val id: String,
    val kind: String,
    val name: String,
    val mime: String,
    val staged: Boolean = false
)

class HomeworkFileException(val code: String) : Exception(code)

object HomeworkFileRules {
    const val MAX_FILES = 6
    const val PHOTO_BYTES = 12 * 1024 * 1024
    const val DOCUMENT_BYTES = 20 * 1024 * 1024
    private val documents = setOf(
        "pdf", "txt", "csv", "rtf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "odp", "zip"
    )
    private val photos = setOf("jpg", "jpeg", "png", "webp", "gif")

    fun cleanName(raw: String): String {
        val base = raw.substringAfterLast('/').substringAfterLast('\\').trim().filter { it >= ' ' && it != '"' && it != '\'' }
        if (base.isEmpty() || base.contains("..")) return ""
        return base.take(80)
    }

    fun extension(name: String): String = name.substringAfterLast('.', "").lowercase()

    fun accept(kind: String, rawName: String, size: Int): String {
        if (size <= 0) throw HomeworkFileException("bad")
        val name = cleanName(rawName)
        if (name.isEmpty() || name.count { it == '.' } != 1) throw HomeworkFileException("bad")
        val ext = extension(name)
        return when (kind) {
            "photo" -> {
                if (size > PHOTO_BYTES || ext !in photos) throw HomeworkFileException(if (size > PHOTO_BYTES) "big" else "bad")
                name
            }
            "document" -> {
                if (size > DOCUMENT_BYTES || ext !in documents) throw HomeworkFileException(if (size > DOCUMENT_BYTES) "big" else "bad")
                name
            }
            else -> throw HomeworkFileException("bad")
        }
    }
}

object HomeworkPhotos {
    fun jpeg(bytes: ByteArray): ByteArray {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        val width = bounds.outWidth
        val height = bounds.outHeight
        if (width <= 0 || height <= 0 || width.toLong() * height > 24_000_000L) throw HomeworkFileException("bad")
        var sample = 1
        while (width / sample > 3200 || height / sample > 3200) sample *= 2
        val decoded = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample })
            ?: throw HomeworkFileException("bad")
        val edge = maxOf(decoded.width, decoded.height).coerceAtLeast(1)
        val scale = minOf(1f, 1600f / edge)
        val target = if (scale < 1f) Bitmap.createScaledBitmap(decoded, (decoded.width * scale).toInt().coerceAtLeast(1), (decoded.height * scale).toInt().coerceAtLeast(1), true) else decoded
        val out = ByteArrayOutputStream()
        try {
            if (!target.compress(Bitmap.CompressFormat.JPEG, 70, out)) throw HomeworkFileException("bad")
        } finally {
            if (target !== decoded) target.recycle()
            decoded.recycle()
        }
        val jpeg = out.toByteArray()
        if (jpeg.size > 4 * 1024 * 1024) throw HomeworkFileException("big")
        return jpeg
    }
}

class HomeworkFileStore(private val root: File) {
    private val gate = Any()

    fun list(homeworkId: Long): List<HomeworkStoredFile> = synchronized(gate) {
        readIndex(itemDir(homeworkId)).map { it.copy(staged = false) }
    }

    fun listDraft(draft: String): List<HomeworkStoredFile> = synchronized(gate) {
        readIndex(draftDir(draft)).map { it.copy(staged = true) }
    }

    fun savedFile(homeworkId: Long, fileId: String): Pair<File, String>? = synchronized(gate) {
        val meta = readIndex(itemDir(homeworkId)).firstOrNull { it.id == fileId } ?: return null
        val file = inside(itemDir(homeworkId), fileId) ?: return null
        if (!file.isFile) return null
        file to meta.mime
    }

    fun stage(draft: String, kind: String, rawName: String, bytes: ByteArray, used: Int = 0): HomeworkStoredFile = synchronized(gate) {
        val existing = readIndex(draftDir(draft))
        if (used + existing.size >= HomeworkFileRules.MAX_FILES) throw HomeworkFileException("full")
        val stored = store(draftDir(draft), kind, rawName, bytes)
        writeIndex(draftDir(draft), existing + stored)
        stored.copy(staged = true)
    }

    fun commit(draft: String, homeworkId: Long, drop: Set<String>) = synchronized(gate) {
        val dir = itemDir(homeworkId)
        val kept = readIndex(dir).filter { it.id !in drop }
        drop.forEach { id -> inside(dir, id)?.takeIf { it.isFile }?.delete() }
        val staged = readIndex(draftDir(draft)).filter { it.id !in drop }
        val room = (HomeworkFileRules.MAX_FILES - kept.size).coerceAtLeast(0)
        val moving = staged.take(room)
        dir.mkdirs()
        moving.forEach { meta ->
            val from = inside(draftDir(draft), meta.id)
            val to = inside(dir, meta.id)
            if (from != null && to != null && from.isFile) from.copyTo(to, overwrite = true)
        }
        writeIndex(dir, (kept + moving).take(HomeworkFileRules.MAX_FILES))
        draftDir(draft).deleteRecursively()
    }

    fun discard(draft: String) = synchronized(gate) {
        draftDir(draft).deleteRecursively()
    }

    fun discardFile(draft: String, fileId: String) = synchronized(gate) {
        val dir = draftDir(draft)
        inside(dir, fileId)?.takeIf { it.isFile }?.delete()
        writeIndex(dir, readIndex(dir).filter { it.id != fileId })
    }

    fun deleteHomework(homeworkId: Long) = synchronized(gate) {
        itemDir(homeworkId).deleteRecursively()
    }

    fun importUri(context: Context, uri: Uri, draft: String, kind: String, used: Int): HomeworkStoredFile {
        val limit = if (kind == "photo") HomeworkFileRules.PHOTO_BYTES else HomeworkFileRules.DOCUMENT_BYTES
        val bytes = context.contentResolver.openInputStream(uri)?.use { readLimited(it, limit) } ?: throw HomeworkFileException("bad")
        return stage(draft, kind, displayName(context, uri), bytes, used)
    }

    private fun store(dir: File, kind: String, rawName: String, bytes: ByteArray): HomeworkStoredFile {
        val accepted = HomeworkFileRules.accept(kind, rawName, bytes.size)
        val (payload, name, mime) = if (kind == "photo") {
            val jpeg = HomeworkPhotos.jpeg(bytes)
            val stem = accepted.substringBeforeLast('.')
            Triple(jpeg, "$stem.jpg", "image/jpeg")
        } else {
            Triple(bytes, accepted, mimeFor(HomeworkFileRules.extension(accepted)))
        }
        val id = UUID.randomUUID().toString()
        dir.mkdirs()
        File(dir, id).writeBytes(payload)
        return HomeworkStoredFile(id, kind, name, mime)
    }

    private fun itemDir(homeworkId: Long) = File(root, "items/$homeworkId")

    private fun draftDir(draft: String): File {
        if (!safeSegment(draft)) throw HomeworkFileException("bad")
        return File(root, "drafts/$draft")
    }

    private fun safeSegment(value: String) =
        value.length in 1..80 && value.all { it in 'a'..'z' || it in 'A'..'Z' || it in '0'..'9' || it == '-' || it == '_' }

    private fun inside(dir: File, id: String): File? {
        if (!safeSegment(id)) return null
        val file = File(dir, id)
        val rootPath = dir.canonicalFile.path + File.separator
        val full = file.canonicalFile.path
        return if (full.startsWith(rootPath)) file else null
    }

    private fun readIndex(dir: File): List<HomeworkStoredFile> {
        val file = File(dir, "index.json")
        if (!file.isFile) return emptyList()
        return runCatching {
            val text = file.readText()
            val out = mutableListOf<HomeworkStoredFile>()
            var cursor = 0
            while (true) {
                val id = jsonField(text, "id", cursor) ?: break
                val kind = jsonField(text, "kind", id.second) ?: break
                val name = jsonField(text, "name", kind.second) ?: break
                val mime = jsonField(text, "mime", name.second) ?: break
                out += HomeworkStoredFile(id.first, kind.first, name.first, mime.first)
                cursor = mime.second
            }
            out.filter { safeSegment(it.id) && File(dir, it.id).isFile }
        }.getOrDefault(emptyList())
    }

    private fun writeIndex(dir: File, files: List<HomeworkStoredFile>) {
        dir.mkdirs()
        val body = buildString {
            append('[')
            files.forEachIndexed { index, file ->
                if (index > 0) append(',')
                append("{\"id\":").append(jsonQuote(file.id))
                append(",\"kind\":").append(jsonQuote(file.kind))
                append(",\"name\":").append(jsonQuote(file.name))
                append(",\"mime\":").append(jsonQuote(file.mime))
                append('}')
            }
            append(']')
        }
        File(dir, "index.json").writeText(body)
    }

    private fun jsonQuote(value: String) = buildString {
        append('"')
        value.forEach { ch ->
            when (ch) {
                '\\' -> append("\\\\")
                '"' -> append("\\\"")
                else -> append(ch)
            }
        }
        append('"')
    }

    private fun jsonField(text: String, key: String, from: Int): Pair<String, Int>? {
        val token = "\"$key\":\""
        val start = text.indexOf(token, from)
        if (start < 0) return null
        var index = start + token.length
        val value = StringBuilder()
        while (index < text.length) {
            val ch = text[index]
            if (ch == '\\') {
                if (index + 1 >= text.length) return null
                value.append(text[index + 1])
                index += 2
                continue
            }
            if (ch == '"') return value.toString() to (index + 1)
            value.append(ch)
            index++
        }
        return null
    }

    private fun readLimited(input: java.io.InputStream, max: Int): ByteArray {
        val out = ByteArrayOutputStream()
        val buf = ByteArray(8192)
        var total = 0
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            total += n
            if (total > max) throw HomeworkFileException("big")
            out.write(buf, 0, n)
        }
        return out.toByteArray()
    }

    private fun displayName(context: Context, uri: Uri): String {
        val fromQuery = context.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        }
        return fromQuery?.takeIf { it.isNotBlank() } ?: uri.lastPathSegment ?: "file"
    }

    private fun mimeFor(ext: String): String = when (ext) {
        "pdf" -> "application/pdf"
        "txt" -> "text/plain"
        "csv" -> "text/csv"
        "rtf" -> "application/rtf"
        "doc" -> "application/msword"
        "docx" -> "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        "xls" -> "application/vnd.ms-excel"
        "xlsx" -> "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        "ppt" -> "application/vnd.ms-powerpoint"
        "pptx" -> "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        "odt" -> "application/vnd.oasis.opendocument.text"
        "ods" -> "application/vnd.oasis.opendocument.spreadsheet"
        "odp" -> "application/vnd.oasis.opendocument.presentation"
        "zip" -> "application/zip"
        "jpg", "jpeg" -> "image/jpeg"
        "png" -> "image/png"
        "webp" -> "image/webp"
        "gif" -> "image/gif"
        else -> "application/octet-stream"
    }

    companion object {
        fun root(filesDir: File, profileKey: String): File {
            val digest = MessageDigest.getInstance("SHA-256").digest(profileKey.toByteArray(Charsets.UTF_8))
            val hex = digest.joinToString("") { "%02x".format(it) }.take(16)
            return File(filesDir, "homework-files/$hex")
        }
    }
}
