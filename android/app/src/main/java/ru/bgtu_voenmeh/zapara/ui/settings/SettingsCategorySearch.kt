package ru.bgtu_voenmeh.zapara.ui.settings

import java.util.Locale

internal object SettingsCategorySearch {
    fun visible(query: String, terms: Map<String, String>): Set<String> {
        val words = query.trim().lowercase(Locale.ROOT).replace('ё', 'е')
            .split(Regex("\\s+")).filter { it.isNotEmpty() }
        if (words.isEmpty()) return terms.keys
        return terms.filter { (key, value) ->
            val searchable = "$key $value".replace('ё', 'е')
            words.all(searchable::contains)
        }.keys
    }
}
