package ru.bgtu_voenmeh.zapara.ui.groups

data class BallotDraftKey(val ownerId: String, val communityId: String, val topicId: String?)

data class BallotDraft(
    val question: String = "",
    val options: List<String> = listOf("", ""),
    val days: Int = 3,
    val composing: Boolean = false,
    val revision: Long = 0
) {
    val hasContent: Boolean get() = question.isNotBlank() || options.any { it.isNotBlank() } || days != 3
}

/** Retains drafts across channel composition and accepts only an exact submitted revision. */
internal class BallotDraftStore {
    private val drafts = HashMap<BallotDraftKey, BallotDraft>()

    fun get(key: BallotDraftKey): BallotDraft = drafts[key] ?: BallotDraft()

    fun edit(key: BallotDraftKey, question: String? = null, options: List<String>? = null,
        days: Int? = null, composing: Boolean? = null): BallotDraft {
        val old = get(key)
        val updated = old.copy(question = question ?: old.question, options = options?.toList() ?: old.options,
            days = days ?: old.days, composing = composing ?: old.composing)
        if (updated == old) return old
        return updated.copy(revision = old.revision + 1).also { drafts[key] = it }
    }

    fun clear(key: BallotDraftKey): BallotDraft = BallotDraft(revision = get(key).revision + 1)
        .also { drafts[key] = it }

    fun acknowledge(key: BallotDraftKey, submittedRevision: Long): Boolean {
        if (get(key).revision != submittedRevision) return false
        clear(key)
        return true
    }
}
