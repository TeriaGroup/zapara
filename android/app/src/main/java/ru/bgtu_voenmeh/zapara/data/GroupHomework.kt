package ru.bgtu_voenmeh.zapara.data

import kotlinx.coroutines.CancellationException

data class LocalHomeworkMark(val id: String, val done: Boolean)

data class GroupCopyMark(val homeworkId: String, val memberId: String, val completed: Boolean)

data class GroupHomeworkBook(val local: List<LocalHomeworkMark>, val copies: List<GroupCopyMark>)

data class HomeworkShareInput(val share: Boolean, val signedIn: Boolean, val communityId: String)

/** Subject and text from the open editor, the group box, and whether this save creates a task. */
data class HomeworkEditorShare(val subject: String, val text: String, val share: Boolean, val isNew: Boolean)

data class HomeworkShareOutcome(val stored: Boolean, val sent: Boolean, val note: String)

object GroupHomework {
    const val SIGN_IN = "Войдите в аккаунт, чтобы отправить домашку группе."
    const val LOCAL_ONLY = "Вы ещё не в группе. Домашка сохранена только на этом устройстве."
    const val FAILED = "На устройстве сохранено. Группе отправить не получилось."
    const val SHARED = "Домашка продублирована всей группе."

    /** Store the editor pair first. The group call receives that same pair and runs only after the local row exists. */
    suspend fun saveEditor(
        editor: HomeworkEditorShare,
        signedIn: Boolean,
        communityId: String,
        saveLocal: suspend (subject: String, text: String) -> Unit,
        send: suspend (subject: String, text: String) -> Unit,
    ): HomeworkShareOutcome {
        val subject = editor.subject.trim()
        val text = editor.text.trim()
        return save(subject, text, HomeworkShareInput(editor.share && editor.isNew, signedIn, communityId), saveLocal, send)
    }

    suspend fun save(
        subject: String,
        text: String,
        input: HomeworkShareInput,
        saveLocal: suspend (String, String) -> Unit,
        send: suspend (String, String) -> Unit,
    ): HomeworkShareOutcome {
        saveLocal(subject, text)
        if (!input.share) return HomeworkShareOutcome(true, false, "")
        if (!input.signedIn) return HomeworkShareOutcome(true, false, SIGN_IN)
        if (input.communityId.isBlank()) return HomeworkShareOutcome(true, false, LOCAL_ONLY)
        return try {
            send(subject, text)
            HomeworkShareOutcome(true, true, SHARED)
        } catch (e: CancellationException) {
            throw e
        } catch (_: Exception) {
            HomeworkShareOutcome(true, false, FAILED)
        }
    }

    fun complete(
        local: List<LocalHomeworkMark>,
        copies: List<GroupCopyMark>,
        actorId: String,
        homeworkId: String,
        completed: Boolean,
    ): GroupHomeworkBook {
        val actor = actorId.trim()
        val copyId = homeworkId.trim()
        return GroupHomeworkBook(
            local = local.map { it.copy() },
            copies = copies.map { row ->
                if (row.homeworkId == copyId && row.memberId == actor) row.copy(completed = completed) else row.copy()
            },
        )
    }
}
