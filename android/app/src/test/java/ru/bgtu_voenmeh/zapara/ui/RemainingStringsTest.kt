package ru.bgtu_voenmeh.zapara.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.w3c.dom.Element
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

class RemainingStringsTest {
    private val valuesDir = File("src/main/res/values")
    private val cyrillic = Regex("[А-Яа-яЁё]")

    private val accountKeys = listOf(
        "account_export", "account_export_download", "account_delete", "account_delete_confirm",
        "account_proof", "account_recovery_email", "account_reset", "account_vk", "account_yandex",
        "account_link", "account_unlink", "account_identities", "account_devices", "account_revoke",
        "account_change_password", "account_current_password", "account_new_password",
    )

    private val communityKeys = listOf(
        "community_title", "community_join", "community_pending", "community_members", "community_staff",
        "community_homework", "community_announcements", "community_polls", "community_vote",
        "community_results", "community_accept", "community_reject", "community_empty",
        "community_need_account", "community_forbidden", "nav_community",
    )

    private val syncKeys = listOf(
        "sync_conflict_title", "sync_keep_local", "sync_keep_server",
        "sync_conflict_body", "sync_expired", "sync_offline",
    )

    @Test fun remaining_string_files_parse_and_are_cyrillic() {
        assertFile("strings_account_lifecycle.xml", accountKeys)
        assertFile("strings_communities.xml", communityKeys)
        assertFile("strings_sync.xml", syncKeys)
    }

    private fun assertFile(name: String, expectedKeys: List<String>) {
        val file = File(valuesDir, name)
        assertTrue(file.path, file.isFile)
        val doc = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val nodes = doc.getElementsByTagName("string")
        assertTrue("$name empty", nodes.length > 0)
        val values = (0 until nodes.length).associate { i ->
            val node = nodes.item(i) as Element
            node.getAttribute("name") to node.textContent
        }
        assertEquals("$name keys", expectedKeys.toSet(), values.keys)
        for ((key, text) in values) {
            assertTrue("$name/$key: $text", cyrillic.containsMatchIn(text))
        }
    }
}
