package ru.bgtu_voenmeh.zapara.ui.legal

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.components.ZTextField
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

@Composable
fun LegalDocumentPage(id: String, onClose: () -> Unit) {
    val assets = LocalContext.current.assets
    val agreement = stringResource(R.string.face_agreement)
    val policy = stringResource(R.string.face_policy)
    var selectedId by rememberSaveable(id) { mutableStateOf(id) }
    val doc = remember(selectedId, agreement, policy) {
        LegalDocuments.open({ assets.open(it) }, selectedId,
            if (selectedId == "agreement") agreement else policy)
    }
    var query by rememberSaveable(id) { mutableStateOf("") }
    var scale by rememberSaveable(id) { mutableFloatStateOf(1f) }
    val paragraphs = remember(doc.body, query) { LegalDocuments.matchingParagraphs(doc.body, query) }
    val listState = rememberLazyListState()
    val scope = rememberCoroutineScope()
    val c = Zapara.colors
    Column(Modifier.fillMaxSize()) {
        if (listState.firstVisibleItemIndex > 0) ZButton(stringResource(R.string.ux100_platform_to_top),
            { scope.launch { listState.animateScrollToItem(0) } },
            modifier = Modifier.fillMaxWidth().padding(horizontal = Zapara.space.l),
            ghost = true, tag = "Legal.ToTop")
        LazyColumn(Modifier.fillMaxSize().testTag("Legal.DocumentList"), state = listState,
            contentPadding = PaddingValues(Zapara.space.l),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            item("controls") {
                Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.face_back), onClose, ghost = true, tag = "Legal.Close")
                    Text(doc.title, style = Zapara.typography.title, color = c.text1, modifier = Modifier.fillMaxWidth())
                    ZButton(if (selectedId == "agreement") policy else agreement, {
                        selectedId = if (selectedId == "agreement") "policy" else "agreement"
                        query = ""
                        scope.launch { listState.scrollToItem(0) }
                    }, ghost = true, tag = "Legal.OtherDocument")
                    ZTextField(query, { query = it }, modifier = Modifier.fillMaxWidth().testTag("Legal.Search"),
                        placeholder = { Text(stringResource(R.string.ux100_platform_legal_search)) }, singleLine = true)
                    if (query.isNotBlank()) {
                        Text(stringResource(R.string.ux100_platform_legal_results, paragraphs.size),
                            style = Zapara.typography.caption, color = c.text2)
                        ZButton(stringResource(R.string.next_teachers_clear), { query = "" }, ghost = true,
                            tag = "Legal.ClearSearch")
                    }
                    androidx.compose.foundation.layout.Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        ZButton(stringResource(R.string.ux100_platform_text_smaller),
                            { scale = (scale - 0.15f).coerceAtLeast(0.85f) }, ghost = true,
                            enabled = scale > 0.85f, tag = "Legal.TextSmaller")
                        ZButton(stringResource(R.string.ux100_platform_text_larger),
                            { scale = (scale + 0.15f).coerceAtMost(1.6f) }, ghost = true,
                            enabled = scale < 1.6f, tag = "Legal.TextLarger")
                    }
                }
            }
            if (paragraphs.isEmpty()) item("empty") {
                Text(stringResource(R.string.ux100_platform_legal_no_match), style = Zapara.typography.body,
                    color = c.text2, modifier = Modifier.testTag("Legal.NoMatch"))
            }
            items(paragraphs.size, key = { "paragraph:$it" }) { index ->
                SelectionContainer {
                    Text(paragraphs[index], style = Zapara.typography.body.copy(
                        fontSize = Zapara.typography.body.fontSize * scale), color = c.text1,
                        modifier = Modifier.fillMaxWidth().testTag("Legal.Paragraph.$index"))
                }
            }
        }
    }
}
