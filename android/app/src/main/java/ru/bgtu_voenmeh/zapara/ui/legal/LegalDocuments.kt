package ru.bgtu_voenmeh.zapara.ui.legal

import java.io.InputStream

data class LegalText(val id: String, val title: String, val body: String)

/** Opens the shared legal files. Settings passes the asset opener; tests pass the same files. */
object LegalDocuments {
    const val AGREEMENT_TITLE = "Пользовательское соглашение"
    const val POLICY_TITLE = "Политика обработки персональных данных"

    fun open(read: (String) -> InputStream, id: String): LegalText {
        val title = if (id == "agreement") AGREEMENT_TITLE else POLICY_TITLE
        val name = if (id == "agreement") "user-agreement.txt" else "privacy-policy.txt"
        val body = read(name).bufferedReader(Charsets.UTF_8).use { it.readText() }.trimStart('\uFEFF').trim()
        return LegalText(id, title, body)
    }
}
