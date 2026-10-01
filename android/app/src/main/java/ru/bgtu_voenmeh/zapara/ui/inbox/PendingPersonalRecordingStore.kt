package ru.bgtu_voenmeh.zapara.ui.inbox

import java.io.File

data class PersonalRecordingScope(val profileId: String, val databaseName: String, val conversationId: String)

data class PendingPersonalRecording(
    val scope: PersonalRecordingScope,
    val kind: String,
    val file: File,
    val durationMs: Int,
    val uncertain: Boolean = false,
    val inFlight: Boolean = false
)

/** In-memory only: the recording stays owned by one profile and one conversation. */
internal class PendingPersonalRecordingStore {
    private val recordings = mutableMapOf<PersonalRecordingScope, PendingPersonalRecording>()

    @Synchronized
    fun get(scope: PersonalRecordingScope): PendingPersonalRecording? = recordings[scope]

    @Synchronized
    fun begin(recording: PendingPersonalRecording): Boolean {
        if (recordings.containsKey(recording.scope)) return false
        recordings[recording.scope] = recording.copy(uncertain = false, inFlight = true)
        return true
    }

    @Synchronized
    fun retry(scope: PersonalRecordingScope): PendingPersonalRecording? {
        val current = recordings[scope] ?: return null
        if (current.inFlight || !current.uncertain || !current.file.isFile) return null
        return current.copy(inFlight = true).also { recordings[scope] = it }
    }

    @Synchronized
    fun markUncertain(scope: PersonalRecordingScope, file: File): Boolean {
        val current = recordings[scope] ?: return false
        if (current.file != file) return false
        recordings[scope] = current.copy(uncertain = true, inFlight = false)
        return true
    }

    /** Removes an acknowledged file from retry state; the request owner deletes it in finally. */
    @Synchronized
    fun removeAccepted(scope: PersonalRecordingScope, file: File): Boolean = removeIfOwned(scope, file)

    @Synchronized
    fun discard(scope: PersonalRecordingScope, file: File): Boolean {
        val current = recordings[scope] ?: return false
        if (current.file != file || current.inFlight) return false
        recordings.remove(scope)
        current.file.delete()
        return true
    }

    @Synchronized
    fun removeCancelled(scope: PersonalRecordingScope, file: File): Boolean {
        if (!removeIfOwned(scope, file)) return false
        file.delete()
        return true
    }

    @Synchronized
    fun clearAndDelete() {
        recordings.values.forEach { it.file.delete() }
        recordings.clear()
    }

    private fun removeIfOwned(scope: PersonalRecordingScope, file: File): Boolean {
        val current = recordings[scope] ?: return false
        if (current.file != file) return false
        recordings.remove(scope)
        return true
    }
}
