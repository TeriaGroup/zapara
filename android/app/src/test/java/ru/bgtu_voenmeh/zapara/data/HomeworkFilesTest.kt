package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files

class HomeworkFilesTest {
    @Test fun names_keep_one_extension_and_drop_a_path() {
        assertEquals("конспект.pdf", HomeworkFileRules.cleanName("C:\\папка\\конспект.pdf"))
        assertEquals("секрет.pdf", HomeworkFileRules.cleanName("../секрет.pdf"))
        assertEquals("", HomeworkFileRules.cleanName("секрет..pdf"))
        assertThrows(HomeworkFileException::class.java) { HomeworkFileRules.accept("document", "работа.pdf.exe", 20) }
        assertEquals("pdf", HomeworkFileRules.extension("Работа.PDF"))
    }

    @Test fun documents_and_photos_have_a_closed_list_and_a_size() {
        assertEquals("урок.pdf", HomeworkFileRules.accept("document", "урок.pdf", 20))
        assertThrows(HomeworkFileException::class.java) { HomeworkFileRules.accept("document", "урок.exe", 20) }
        assertThrows(HomeworkFileException::class.java) { HomeworkFileRules.accept("photo", "снимок.jpg", HomeworkFileRules.PHOTO_BYTES + 1) }
        assertEquals("big", assertThrows(HomeworkFileException::class.java) {
            HomeworkFileRules.accept("document", "папка.zip", HomeworkFileRules.DOCUMENT_BYTES + 1)
        }.code)
    }

    @Test fun a_staged_document_is_kept_and_a_removed_file_is_dropped() {
        val root = Files.createTempDirectory("zapara-hw").toFile()
        val store = HomeworkFileStore(root)
        val staged = store.stage("draft", "document", "конспект.pdf", "привет".toByteArray())
        val extra = store.stage("draft", "document", "лишнее.txt", "нет".toByteArray())
        store.discardFile("draft", extra.id)
        val old = store.stage("keep", "document", "старое.txt", "a".toByteArray())
        store.commit("keep", 7, emptySet())
        store.commit("draft", 7, setOf(old.id))
        val left = store.list(7)
        assertEquals(listOf(staged.name), left.map { it.name })
        assertEquals("document", left.single().kind)
        assertTrue(store.savedFile(7, left.single().id)!!.first.readBytes().contentEquals("привет".toByteArray()))
        store.deleteHomework(7)
        assertTrue(store.list(7).isEmpty())
    }

    @Test fun a_crafted_id_stays_inside_the_homework_folder() {
        val root = Files.createTempDirectory("zapara-hw").toFile()
        val store = HomeworkFileStore(root)
        val outside = Files.createTempDirectory("zapara-outside").toFile()
        val victim = File(outside, "secret.txt")
        victim.writeText("keep")
        store.commit("draft-safe1", 7, setOf("../secret.txt", victim.absolutePath))
        assertTrue(victim.isFile)
        assertEquals(null, store.savedFile(7, "../secret.txt"))
    }
}
