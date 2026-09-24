package ru.bgtu_voenmeh.zapara.ui.groups

import android.app.Application
import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.Intent
import androidx.core.content.FileProvider
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.communities.CommunityValidation
import java.io.File
import java.io.IOException

internal object GroupMediaFiles {
    private const val maxBytes = 8 * 1024 * 1024
    private const val maxCachedFiles = 32
    private const val maxCachedBytes = 64L * 1024 * 1024

    @Synchronized
    fun save(cacheDir: File, messageId: String, name: String, bytes: ByteArray): File {
        require(bytes.isNotEmpty() && bytes.size <= maxBytes)
        val id = CommunityValidation.id(messageId)
        val root = cacheDir.canonicalFile
        val directory = File(root, "group-media")
        if (!directory.isDirectory && !directory.mkdirs()) throw IOException("Cannot create media cache")
        if (directory.canonicalFile != directory.absoluteFile || directory.canonicalFile.parentFile != root) {
            throw IOException("Media cache is outside app cache")
        }
        val file = File(directory, "$id-${safeName(name)}")
        val temporary = File.createTempFile("group-media-", ".tmp", directory)
        try {
            temporary.writeBytes(bytes)
            if (file.exists() && file.canonicalFile.parentFile != directory.canonicalFile) {
                throw IOException("Media cache file is outside app cache")
            }
            if (file.exists() && !file.delete()) throw IOException("Cannot replace media cache")
            if (!temporary.renameTo(file)) throw IOException("Cannot save media cache")
        } finally {
            temporary.delete()
        }
        prune(directory, file)
        return file
    }

    private fun prune(directory: File, opened: File) {
        val canonicalDirectory = directory.canonicalFile
        val files = directory.listFiles()?.filter {
            it.isFile && it.canonicalFile.parentFile == canonicalDirectory
        } ?: throw IOException("Cannot list media cache")
        var count = files.size
        var total = files.sumOf { it.length() }
        for (old in files.filter { it != opened }.sortedWith(compareBy<File> { it.lastModified() }.thenBy { it.name })) {
            if (count <= maxCachedFiles && total <= maxCachedBytes) break
            val length = old.length()
            if (!old.delete()) throw IOException("Cannot prune media cache")
            count--
            total -= length
        }
        if (count > maxCachedFiles || total > maxCachedBytes) throw IOException("Media cache limit exceeded")
    }

    fun mimeType(kind: String, name: String): String {
        val extension = name.substringAfterLast('.', "").lowercase()
        return when (kind) {
            "image" -> when (extension) {
                "jpg", "jpeg" -> "image/jpeg"
                "png" -> "image/png"
                "gif" -> "image/gif"
                "webp" -> "image/webp"
                "heic", "heif" -> "image/heic"
                "avif" -> "image/avif"
                else -> "image/*"
            }
            "video" -> when (extension) {
                "mp4", "m4v" -> "video/mp4"
                "webm" -> "video/webm"
                "3gp" -> "video/3gpp"
                "mkv" -> "video/x-matroska"
                else -> "video/*"
            }
            else -> when (extension) {
                "pdf" -> "application/pdf"
                "txt" -> "text/plain"
                "csv" -> "text/csv"
                "json" -> "application/json"
                "zip" -> "application/zip"
                "doc" -> "application/msword"
                "docx" -> "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                "xls" -> "application/vnd.ms-excel"
                "xlsx" -> "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                "ppt" -> "application/vnd.ms-powerpoint"
                "pptx" -> "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                else -> "application/octet-stream"
            }
        }
    }

    private fun safeName(name: String): String {
        val leaf = name.trim().substringAfterLast('/').substringAfterLast('\\')
        return leaf.map { c -> if (c.isLetterOrDigit() || c in "._- ") c else '_' }
            .joinToString("")
            .trim(' ', '.')
            .take(80)
            .trimEnd(' ', '.')
            .ifBlank { "attachment" }
    }
}

internal class GroupMediaViewer(private val app: Application) {
    suspend fun open(message: GroupMessageUi, bytes: ByteArray): Boolean {
        val file = withContext(Dispatchers.IO) {
            GroupMediaFiles.save(app.cacheDir, message.id, message.body, bytes)
        }
        val uri = FileProvider.getUriForFile(app, "${app.packageName}.fileprovider", file)
        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, GroupMediaFiles.mimeType(message.kind, file.name))
            clipData = ClipData.newUri(app.contentResolver, file.name, uri)
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        return try {
            app.startActivity(intent)
            true
        } catch (_: ActivityNotFoundException) {
            false
        } catch (_: SecurityException) {
            false
        }
    }
}
