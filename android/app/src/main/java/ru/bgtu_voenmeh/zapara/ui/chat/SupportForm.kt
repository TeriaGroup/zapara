package ru.bgtu_voenmeh.zapara.ui.chat

object SupportForm {
    data class Note(val author: String, val body: String)
    data class Result(val thread: List<Note>, val error: String?)

    fun submit(signedIn: Boolean, thread: List<Note>, subject: String, body: String, signInError: String, describeError: String): Result {
        if (!signedIn) return Result(thread, signInError)
        val theme = subject.trim()
        val text = body.trim()
        if (theme.length < 3 || text.length < 3) return Result(thread, describeError)
        return Result(thread + Note("user", text), null)
    }

    fun reply(thread: List<Note>, body: String): List<Note> = thread + Note("operator", body.trim())
}
