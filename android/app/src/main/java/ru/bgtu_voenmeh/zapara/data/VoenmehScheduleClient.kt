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
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.obj
import java.time.LocalTime

class VoenmehScheduleClient(
    private val get: (String) -> String = Companion::httpGet
) {
    suspend fun fetchSchedule(groupNames: Collection<String> = emptyList()): ParsedSchedule = coroutineScope {
        val meta = VoenmehScheduleParser.parseMeta(get(META_URL))
        val fetchList = groupNames.map { it.trim() }.filter { it.isNotEmpty() }.map { requested ->
            meta.groups.firstOrNull { it.equals(requested, ignoreCase = true) }
                ?: throw IllegalStateException("Группа $requested не найдена в актуальном расписании университета")
        }.distinct()
        val slots = Semaphore(6)
        val loaded = fetchList.map { name ->
            async {
                slots.withPermit {
                    val url = "$LESSONS_URL?name=${encode(name)}&kind=group"
                    name to checkedLessons(get(url), name)
                }
            }
        }.awaitAll()
        if (fetchList.isNotEmpty()) {
            val after = VoenmehScheduleParser.parseMeta(get(META_URL))
            if (after.groups.toSet() != meta.groups.toSet() || after.copy(groups = meta.groups) != meta) {
                throw IllegalStateException("Расписание изменилось во время загрузки. Повторите обновление")
            }
        }
        VoenmehScheduleParser.assemble(meta, loaded)
    }

    private fun checkedLessons(json: String, name: String): List<Lesson> {
        val lessons = VoenmehScheduleParser.parseLessons(json, name)
        val payload = StrictJson.parse(json.trimStart('\uFEFF')).obj()
        val rows = (payload.fields["lessons"] as? JsonValue.Arr)?.items
            ?: throw IllegalStateException(TimetablePayload.NOT_XML)
        val returnedName = (payload.fields["name"] as? JsonValue.Str)?.value?.trim()
        if (returnedName != name || lessons.size != rows.size ||
            rows.any { row -> ((row as? JsonValue.Obj)?.fields?.get("subject") as? JsonValue.Str)?.value.isNullOrBlank() } ||
            lessons.any { lesson -> runCatching { LocalTime.parse(lesson.timeStart) }.isFailure }) {
            throw IllegalStateException("Некорректные данные расписания группы $name. Локальные данные сохранены")
        }
        return lessons
    }

    companion object {
        const val ORIGIN = "https://voenmeh.su/schedule"
        const val META_URL = "https://voenmeh.su/api/schedule/meta"
        const val LESSONS_URL = "https://voenmeh.su/api/schedule/lessons"

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
