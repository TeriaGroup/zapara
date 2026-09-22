package ru.bgtu_voenmeh.zapara.ui.homework

import kotlinx.coroutines.CancellationException
import ru.bgtu_voenmeh.zapara.AppContainer
import ru.bgtu_voenmeh.zapara.data.GroupHomework
import ru.bgtu_voenmeh.zapara.data.HomeworkEditorShare
import ru.bgtu_voenmeh.zapara.data.HomeworkShareOutcome

internal suspend fun shareSavedHomework(
    container: AppContainer,
    editor: HomeworkEditorState,
    persist: suspend () -> Unit,
): HomeworkShareOutcome {
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
        val groupId = container.repo.settings().myGroupId.orEmpty()
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
        { _, _ -> persist() },
    ) { sentSubject, sentText ->
        val client = container.communities ?: error("communities")
        val access = token ?: error("token")
        client.shareHomework(access, communityId, sentSubject, sentText, 0)
    }
}
