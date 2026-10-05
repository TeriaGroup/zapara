package ru.bgtu_voenmeh.zapara.ui.shell

internal class ShellProjectionGate {
    private var version = 0L
    fun begin(): Long = ++version
    fun invalidate() { version++ }
    fun current(request: Long): Boolean = request == version
    fun mayPublish(request: Long, readGroup: String?, savedGroup: String?, ownerCurrent: Boolean): Boolean =
        ownerCurrent && current(request) && readGroup == savedGroup
}
