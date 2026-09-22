package ru.bgtu_voenmeh.zapara.ui.legal

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun LegalDocumentPage(id: String, onClose: () -> Unit) {
    val assets = LocalContext.current.assets
    val doc = remember(id) { LegalDocuments.open({ assets.open(it) }, id) }
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(Zapara.space.l)) {
            item {
                ZButton("Назад", onClose, ghost = true, tag = "Legal.Close")
                Text(doc.title, style = Zapara.typography.title, color = c.text1, modifier = Modifier.fillMaxWidth())
                Text(doc.body, style = Zapara.typography.body, color = c.text1, modifier = Modifier.fillMaxWidth())
            }
        }
    }
}
