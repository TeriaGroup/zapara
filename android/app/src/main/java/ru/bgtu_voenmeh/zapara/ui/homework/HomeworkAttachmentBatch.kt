package ru.bgtu_voenmeh.zapara.ui.homework

import android.net.Uri
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.HomeworkFileRules
import ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile

internal data class AttachmentBatchResult(val added: Int, val failed: Int)

internal fun loadEditorFiles(container: AppContainer, homeworkId: Long?):
    Pair<List<HomeworkStoredFile>, Set<String>> {
    if (homeworkId == null) return emptyList<HomeworkStoredFile>() to emptySet()
    val files = container.homeworkFiles.list(homeworkId)
    val missing = files.filter { runCatching {
        container.homeworkFiles.savedFile(homeworkId, it.id)
    }.getOrNull() == null }
        .map { it.id }.toSet()
    return files to missing
}

/** Import independently: one invalid file must not discard the other chosen files. */
internal suspend fun importHomeworkAttachmentBatch(container: AppContainer,
    editor: HomeworkEditorState, kind: String, uris: List<Uri>, adopt: (HomeworkStoredFile) -> Boolean
): AttachmentBatchResult {
    val distinct = uris.distinct()
    val capacity = (HomeworkFileRules.MAX_FILES - editor.files.size).coerceAtLeast(0)
    var added = 0
    var failed = (distinct.size - capacity).coerceAtLeast(0)
    for (uri in distinct.take(capacity)) {
        var imported: HomeworkStoredFile? = null
        var adopted = false
        try {
            val saved = container.homeworkFiles.importUri(container.app, uri, editor.draft,
                kind, editor.files.count { !it.staged })
            imported = saved
            adopted = adopt(saved)
            if (adopted) added++ else failed++
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { failed++ }
        finally {
            if (!adopted) imported?.let { saved -> withContext(NonCancellable) {
                runCatching { container.homeworkFiles.discardFile(editor.draft, saved.id) }
            } }
        }
    }
    return AttachmentBatchResult(added, failed)
}
