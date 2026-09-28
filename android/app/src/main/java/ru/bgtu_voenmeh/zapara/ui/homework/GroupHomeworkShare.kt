package ru.bgtu_voenmeh.zapara.ui.homework

import kotlinx.coroutines.CancellationException
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.GroupHomework
import ru.bgtu_voenmeh.zapara.data.HomeworkEditorShare
import ru.bgtu_voenmeh.zapara.data.HomeworkShareOutcome
import ru.bgtu_voenmeh.zapara.data.communities.Classmate
import ru.bgtu_voenmeh.zapara.data.communities.GroupDesk
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkAudience
import java.time.Instant

data class HomeworkPublishSnapshot(val communityId: String, val authorId: String?, val title: String, val body: String, val deadlineAt: Instant?, val audience: HomeworkAudience?, val operationId: String?)

internal suspend fun retryHomeworkPublication(container: AppContainer, snapshot: HomeworkPublishSnapshot) {
    val operationId = snapshot.operationId ?: error("Safe retry unavailable")
    val token = container.accessToken() ?: error("No session")
    val currentAuthor = container.communities?.groupHome(token, snapshot.communityId)?.classmates?.firstOrNull { it.self }?.userId
    if (snapshot.authorId == null || snapshot.authorId != currentAuthor) error("Account changed")
    container.communities?.shareHomework(token, snapshot.communityId, snapshot.title, snapshot.body, 0, snapshot.deadlineAt,
        audience = snapshot.audience, operationId = operationId) ?: error("No client")
}

data class HomeworkShareContext(val communityId: String, val groupName: String, val desk: GroupDesk, val people: List<Classmate>) {
    val supported: Boolean get() = desk.capabilities.homeworkAudience
}

internal suspend fun loadHomeworkShareContext(container: AppContainer, groupId: String?): HomeworkShareContext? {
    val token = container.accessToken() ?: return null
    val client = container.communities ?: return null
    val group = groupId ?: container.repo.settings().myGroupId ?: return null
    val community = client.list(token, group).firstOrNull { !it.role.isNullOrBlank() } ?: return null
    val desk = client.desk(token, community.communityId)
    val home = client.groupHome(token, community.communityId)
    return HomeworkShareContext(community.communityId, home.name, desk, home.classmates)
}

internal class HomeworkScheduleChanged : IllegalStateException("homework_schedule_changed")

internal suspend fun shareSavedHomework(
    container: AppContainer,
    editor: HomeworkEditorState,
    onShareAttempt: (HomeworkPublishSnapshot) -> Unit = {},
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
        editor.shareContext?.communityId ?: run {
        val groupId = editor.scheduleGroupId ?: container.repo.settings().myGroupId.orEmpty()
        container.communities?.list(token!!, groupId)?.firstOrNull { !it.role.isNullOrBlank() }?.communityId.orEmpty()
        }
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
        if (editor.audience.selected && (editor.shareContext?.supported != true || !editor.audience.valid())) error("Selected audience unavailable")
        val snapshot = HomeworkPublishSnapshot(communityId, editor.shareContext?.people?.firstOrNull { it.self }?.userId, sentSubject, sentText,
            expectedDue?.atStartOfDay(java.time.ZoneId.systemDefault())?.plusDays(1)?.minusNanos(1)?.toInstant(),
            editor.audience.takeIf { editor.shareContext?.supported == true }, editor.operationId.takeIf { editor.shareContext?.supported == true })
        onShareAttempt(snapshot)
        client.shareHomework(access, snapshot.communityId, snapshot.title, snapshot.body, 0, snapshot.deadlineAt,
            audience = snapshot.audience, operationId = snapshot.operationId)
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
        onShareAttempt = { snapshot -> progress(current.copy(shareAttempted = true, shareRequest = snapshot)) }) {
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
    return when {
        editor.share && editor.shareAttempted -> outcome.copy(note = container.app.getString(ru.bgtu_voenmeh.zapara.R.string.homework_ux_share_check))
        outcome.sent && editor.audience.selected -> outcome.copy(note = container.app.getString(ru.bgtu_voenmeh.zapara.R.string.homework_share_selected_success))
        else -> outcome
    }
}
