package ru.bgtu_voenmeh.zapara.ui.legal

import java.io.InputStream

data class LegalText(val id: String, val title: String, val body: String)

/** Opens the shared legal files. Settings passes the asset opener; tests pass the same files. */
object LegalDocuments {
    fun open(read: (String) -> InputStream, id: String, title: String): LegalText {
        val name = if (id == "agreement") "user-agreement.txt" else "privacy-policy.txt"
        val body = read(name).bufferedReader(Charsets.UTF_8).use { it.readText() }.trimStart('\uFEFF').trim()
        return LegalText(id, title, body)
    }

    fun paragraphs(body: String): List<String> = body.split(Regex("\\n\\s*\\n"))
        .map(String::trim).filter(String::isNotEmpty)

    fun matchingParagraphs(body: String, query: String): List<String> {
        val paragraphs = paragraphs(body)
        val words = query.trim().lowercase().replace('ё', 'е').split(Regex("\\s+"))
            .filter(String::isNotEmpty)
        if (words.isEmpty()) return paragraphs
        return paragraphs.filter { paragraph ->
            val normalized = paragraph.lowercase().replace('ё', 'е')
            words.all(normalized::contains)
        }
    }
}
