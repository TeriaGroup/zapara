package ru.bgtu_voenmeh.zapara.ui.chat

import java.util.Locale

internal fun normalizeChatSearch(value: String): String =
    value.lowercase(Locale.ROOT).replace('ё', 'е').trim()

internal fun chatSearchWords(query: String): List<String> =
    normalizeChatSearch(query).split(Regex("\\s+")).filter(String::isNotEmpty)
