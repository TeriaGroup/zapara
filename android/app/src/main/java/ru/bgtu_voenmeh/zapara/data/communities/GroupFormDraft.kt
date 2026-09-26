package ru.bgtu_voenmeh.zapara.data.communities

import java.time.Instant

data class GroupFormDraft(val title: String, val description: String, val deadline: Instant?, val anonymous: Boolean, val questions: List<GroupFormQuestion>) {
    fun normalized() = copy(title = title.trim(), questions = questions.map { q -> q.copy(title = q.title.trim(), options = if (q.kind in setOf("shortText", "longText")) emptyList() else q.options.map(String::trim).filter(String::isNotEmpty)) })
    fun problem(now: Instant): String? {
        val value = normalized()
        if (!text(value.title, 120, false) || value.title.isBlank()) return "title"
        if (!text(value.description, 2000, true)) return "description"
        if (value.deadline?.let { it <= now } == true) return "deadline"
        if (value.questions.size !in 1..30 || value.questions.map { it.questionId }.distinct().size != value.questions.size) return "questions"
        value.questions.forEach { q ->
            if (runCatching { CommunityValidation.id(q.questionId) }.isFailure || q.title.isBlank() || !text(q.title,400,false) || q.kind !in setOf("shortText","longText","singleChoice","multipleChoice")) return "question"
            if (q.kind in setOf("singleChoice","multipleChoice") && (q.options.size !in 2..16 || q.options.any { !text(it,120,false) || it.isBlank() } || q.options.distinct().size != q.options.size)) return "options"
        }
        if (value.body().toByteArray(Charsets.UTF_8).size > CommunityValidation.RequestBytes) return "size"
        return null
    }
    fun body(): String = "{\"title\":${quote(title)},\"description\":${quote(description)},\"deadlineAt\":${deadline?.let(CommunityUtc::format)?.let(::quote) ?: "null"},\"anonymous\":$anonymous,\"questions\":[" + questions.joinToString(",") { q -> "{\"questionId\":${quote(q.questionId)},\"title\":${quote(q.title)},\"kind\":${quote(q.kind)},\"required\":${q.required},\"options\":[" + q.options.joinToString(",", transform = ::quote) + "]}" } + "]}"
    companion object {
        private fun text(value: String, max: Int, multiline: Boolean) = value.length <= max && value.none { c -> c.isISOControl() && !(multiline && c in "\n\r\t") }
        private fun quote(value: String): String = "\"" + value.replace("\\","\\\\").replace("\"","\\\"").replace("\n","\\n").replace("\r","\\r").replace("\t","\\t") + "\""
    }
}
