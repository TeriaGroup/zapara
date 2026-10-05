package ru.bgtu_voenmeh.zapara.ui.groups

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.io.RandomAccessFile

class GroupMediaFilesTest {
    @get:Rule val folder = TemporaryFolder()
    private val messageId = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee1"

    @Test
    fun savesMediaOnlyInsideItsPrivateCacheDirectoryWithASafeBasename() {
        val cache = folder.root
        val bytes = byteArrayOf(4, 5, 6)
        val file = GroupMediaFiles.save(cache, messageId, "../../shared\\photo:one.png", bytes)
        val mediaDir = requireNotNull(file.parentFile)
        assertEquals("$messageId-photo_one.png", file.name)
        assertEquals(cache.canonicalFile, requireNotNull(mediaDir.parentFile).canonicalFile)
        assertEquals("group-media", mediaDir.name)
        assertArrayEquals(bytes, file.readBytes())

        val blank = GroupMediaFiles.save(cache, messageId, "..\\...", byteArrayOf(7))
        assertEquals("$messageId-attachment", blank.name)
        assertTrue(blank.exists())
    }

    @Test
    fun viewerMimeTypeMatchesMediaKindAndSafeExtension() {
        assertEquals("image/png", GroupMediaFiles.mimeType("image", "photo.PNG"))
        assertEquals("image/*", GroupMediaFiles.mimeType("image", "photo.pdf"))
        assertEquals("video/mp4", GroupMediaFiles.mimeType("video", "clip.mp4"))
        assertEquals("application/pdf", GroupMediaFiles.mimeType("file", "report.pdf"))
        assertEquals("application/octet-stream", GroupMediaFiles.mimeType("file", "archive.unknown"))
    }

    @Test
    fun removesOldestCachedFilesBeyondThirtyTwoWithoutTouchingOtherCacheFiles() {
        val cache = folder.root
        val mediaDir = File(cache, "group-media").also { assertTrue(it.mkdir()) }
        val outside = File(cache, "outside.txt").also { it.writeText("keep") }
        val old = (0 until 34).map { index ->
            File(mediaDir, "old-$index").also {
                it.writeByte(index)
                assertTrue(it.setLastModified(1_700_000_000_000L + index * 1_000))
            }
        }

        val opened = GroupMediaFiles.save(cache, messageId, "photo.png", byteArrayOf(1))

        assertTrue(opened.exists())
        assertEquals(32, mediaDir.listFiles()!!.size)
        assertTrue(old.take(3).none { it.exists() })
        assertTrue(old.drop(3).all { it.exists() })
        assertEquals("keep", outside.readText())
    }

    @Test
    fun keepsTheOpenedFileWhilePruningTheCacheBelowSixtyFourMib() {
        val cache = folder.root
        val mediaDir = File(cache, "group-media").also { assertTrue(it.mkdir()) }
        val old = (0 until 9).map { index ->
            File(mediaDir, "large-$index").also {
                RandomAccessFile(it, "rw").use { stream -> stream.setLength(8L * 1024 * 1024) }
                assertTrue(it.setLastModified(1_700_000_000_000L + index * 1_000))
            }
        }

        val opened = GroupMediaFiles.save(cache, messageId, "note.pdf", byteArrayOf(9))

        assertArrayEquals(byteArrayOf(9), opened.readBytes())
        assertTrue(mediaDir.listFiles()!!.sumOf { it.length() } <= 64L * 1024 * 1024)
        assertEquals(7, old.count { it.exists() })
        assertTrue(old.take(2).none { it.exists() })
    }
}

private fun File.writeByte(value: Int) = writeBytes(byteArrayOf(value.toByte()))
