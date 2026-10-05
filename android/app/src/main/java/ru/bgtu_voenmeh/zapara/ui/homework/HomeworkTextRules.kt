package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.sync.SyncValidation

internal object HomeworkTextRules {
    const val limit = 4000
    fun scalars(value: String): Int = value.codePointCount(0, value.length)
    fun valid(value: String): Boolean = value.trim().isNotEmpty() && scalars(value) <= limit &&
        runCatching { SyncValidation.text(value, limit) }.isSuccess
}
