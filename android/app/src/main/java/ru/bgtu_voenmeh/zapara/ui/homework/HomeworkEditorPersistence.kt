package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.HomeworkStoredFile

/** Retain the written row identity before committing files, so a retry updates the same task. */
internal fun persistHomeworkEditor(
    editor: HomeworkEditorState,
    onProgress: (HomeworkEditorState) -> Unit,
    saveLocal: (Long?) -> Long,
    saveFiles: (Long) -> List<HomeworkStoredFile>
): HomeworkEditorState {
    val id = saveLocal(editor.persistedId ?: editor.id)
    val written = editor.copy(persistedId = id)
    onProgress(written)
    val completed = written.copy(files = saveFiles(id), removed = emptySet())
    onProgress(completed)
    return completed
}
