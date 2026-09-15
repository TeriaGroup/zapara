package ru.bgtu_voenmeh.zapara.data

import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

class VoenmehScheduleClient(
    private val get: (String) -> String = Companion::httpGet
) {
    suspend fun fetchSchedule(groupNames: Collection<String> = emptyList()): ParsedSchedule = coroutineScope {
        val meta = VoenmehScheduleParser.parseMeta(get(META_URL))
        val fetchList = if (groupNames.isEmpty()) {
            meta.groups
        } else {
            groupNames.map { it.trim() }.filter { it.isNotEmpty() }.distinct()
        }
        val slots = Semaphore(6)
        val loaded = fetchList.map { name ->
            async {
                slots.withPermit {
                    val url = "$LESSONS_URL?name=${encode(name)}&type=group"
                    name to try {
                        VoenmehScheduleParser.parseLessons(get(url), name)
                    } catch (_: Exception) {
                        emptyList()
                    }
                }
            }
        }.awaitAll()
        VoenmehScheduleParser.assemble(meta, loaded)
    }

    companion object {
        const val ORIGIN = "https://voenmeh.ru/schedule"
        const val META_URL = "https://voenmeh.ru/api/schedule/meta"
        const val LESSONS_URL = "https://voenmeh.ru/api/schedule/lessons"

        fun encode(value: String): String =
            URLEncoder.encode(value, StandardCharsets.UTF_8.name()).replace("+", "%20")

        fun httpGet(url: String): String {
            val conn = URL(url).openConnection() as HttpURLConnection
            try {
                conn.setRequestProperty("User-Agent", "Mozilla/5.0 (Linux; Android) Zapara/1.0")
                conn.setRequestProperty("Accept", "application/json")
                conn.connectTimeout = 20_000
                conn.readTimeout = 30_000
                conn.connect()
                val stream = if (conn.responseCode in 200..299) conn.inputStream else conn.errorStream
                val body = stream?.bufferedReader(Charsets.UTF_8)?.readText().orEmpty()
                if (conn.responseCode == HttpURLConnection.HTTP_NOT_FOUND) return """{"lessons":[]}"""
                if (conn.responseCode != HttpURLConnection.HTTP_OK) {
                    throw IllegalStateException("HTTP ${conn.responseCode}")
                }
                return body
            } finally {
                conn.disconnect()
            }
        }
    }
}
