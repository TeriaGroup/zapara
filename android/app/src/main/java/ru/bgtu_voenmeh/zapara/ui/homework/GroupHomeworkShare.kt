package ru.bgtu_voenmeh.zapara.ui.homework

import kotlinx.coroutines.CancellationException
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.GroupHomework
import ru.bgtu_voenmeh.zapara.data.HomeworkEditorShare
import ru.bgtu_voenmeh.zapara.data.HomeworkShareOutcome

internal class HomeworkScheduleChanged : IllegalStateException("homework_schedule_changed")

internal suspend fun shareSavedHomework(
    container: AppContainer,
    editor: HomeworkEditorState,
    onShareAttempt: () -> Unit = {},
    persist: suspend () -> Unit,
): HomeworkShareOutcome {
    val expectedDue = editor.dueFor(editor.n,editor.text)
    val wants = editor.share && editor.id == null
    val token = if (!wants || container.profile.isGuest) null else try {
        container.accessToken()
    } catch (e: CancellationException) {
        throw e
    } catch (_: Exception) {
        null
    }
    val signedIn = !token.isNullOrBlank()
    val communityId = if (!wants || !signedIn) "" else try {
        val groupId = editor.scheduleGroupId ?: container.repo.settings().myGroupId.orEmpty()
        container.communities?.list(token!!, groupId)?.firstOrNull { !it.role.isNullOrBlank() }?.communityId.orEmpty()
    } catch (e: CancellationException) {
        throw e
    } catch (_: Exception) {
        ""
    }
    return GroupHomework.saveEditor(
        HomeworkEditorShare(editor.subjectRaw, editor.text, editor.share, editor.id == null),
        signedIn,
        communityId,
        { _, _ ->
            val anchor = editor.id?.let { container.homework.getById(it)?.createdAt } ?: editor.creationAnchor(container.clock().toLocalDate())
            val currentDue = container.homework.computeDueDate(ru.bgtu_voenmeh.zapara.data.Parity.normalizeSubject(editor.subjectRaw),anchor,editor.n)
            if (!editor.matchesSaveContext(container.repo.settings().myGroupId,currentDue)) throw HomeworkScheduleChanged()
            persist()
        },
    ) { sentSubject, sentText ->
        val client = container.communities ?: error("communities")
        val access = token ?: error("token")
        onShareAttempt()
        client.shareHomework(access, communityId, sentSubject, sentText, 0, expectedDue?.atStartOfDay(java.time.ZoneId.systemDefault())?.plusDays(1)?.minusNanos(1)?.toInstant())
    }
}

internal suspend fun saveHomeworkEditor(
    container: AppContainer,
    editor: HomeworkEditorState,
    onProgress: (HomeworkEditorState) -> Unit
): HomeworkShareOutcome {
    var current = editor
    fun progress(value: HomeworkEditorState) { current = value; onProgress(value) }
    val outcome = shareSavedHomework(container, editor.copy(share = editor.share && !editor.shareAttempted),
        onShareAttempt = { progress(current.copy(shareAttempted = true)) }) {
        persistHomeworkEditor(current, ::progress, saveLocal = { existingId ->
            if (existingId == null) {
                container.homework.addHomework(editor.subjectRaw, editor.text.trim(), editor.n, editor.creationAnchor(container.clock().toLocalDate()))
            } else {
                val existing = container.homework.getById(existingId) ?: error("homework_missing")
                if (editor.hasChanges(existing)) container.homework.updateHomework(existingId, editor.text.trim(), editor.n)
                existingId
            }
        }, saveFiles = { id ->
            if (editor.draft.isNotEmpty()) container.homeworkFiles.commit(editor.draft, id, editor.removed)
            container.homeworkFiles.list(id)
        })
    }
    return if (editor.share && editor.shareAttempted)
        outcome.copy(note = container.app.getString(ru.bgtu_voenmeh.zapara.R.string.homework_ux_share_check)) else outcome
}
