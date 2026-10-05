package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.sync.*
import ru.bgtu_voenmeh.zapara.ui.settings.SyncConflictCard
import ru.bgtu_voenmeh.zapara.ui.theme.ThemeChoice
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme
import java.time.Instant
import java.util.UUID

class PrivateSyncConflictUiTest {
    @get:Rule val compose = createComposeRule()

    @Test fun both_versions_are_readable_and_each_button_submits_its_explicit_choice() {
        val now = Instant.parse("2026-09-20T12:00:00Z")
        val id = UUID.randomUUID()
        val op = UUID.randomUUID()
        val local = HomeworkValue("Математика", "математика", "Локальный текст", 2, now, null)
        val remote = SyncRecord("homework", id, 2, false, now, local.copy(text = "Серверный текст"))
        val conflict = SyncConflict(PrivateSyncOutboxEntry(op, "homework", id, 1, "upsert", "conflict", 1, null, now),
            local, remote, null, UUID.randomUUID(), true)
        val choices = mutableListOf<Boolean>()
        compose.setContent {
            ZaparaTheme(ThemeChoice.Light) {
                Column(Modifier.verticalScroll(rememberScrollState())) { SyncConflictCard(conflict, false) { choices.add(it) } }
            }
        }
        val tag = "Sync.Conflict.$op"
        compose.onNodeWithTag("$tag.Local").assertTextContains("Локальный текст", substring = true)
        compose.onNodeWithTag("$tag.Server").assertTextContains("Серверный текст", substring = true)
        compose.onNodeWithTag("$tag.KeepLocal").performScrollTo().performClick()
        compose.onNodeWithTag("$tag.KeepServer").performScrollTo().performClick()
        compose.runOnIdle { assertEquals(listOf(true, false), choices) }
    }
}
