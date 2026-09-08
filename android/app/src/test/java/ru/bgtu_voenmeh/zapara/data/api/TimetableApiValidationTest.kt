package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Test
import java.net.URI

class TimetableApiValidationTest {
    private fun expectInvalidSchedule(mutated: String) = runBlocking {
        val http = FakeHttp { call ->
            jsonReply(if (call.url.substringBefore('?').endsWith("/groups")) catalogJson() else mutated)
        }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        try {
            client.fetch(listOf("a"))
            fail(mutated.take(80))
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.InvalidPayload, e.failure)
        }
    }

    @Test
    fun rejects_invalid_timetable() {
        val good = scheduleJson()
        expectInvalidSchedule(good.replace(PIN, NEW_PIN))
        expectInvalidSchedule(good.replace("Тестовый семестр", "Другой"))
        expectInvalidSchedule(good.replace("\"lessonCount\":1", "\"lessonCount\":2"))
        expectInvalidSchedule(good.replace("лек елка тест", "display name"))
        expectInvalidSchedule(good.replace("\"subjectRaw\":", "\"missingRaw\":"))
        expectInvalidSchedule(good.replace("\"subjectRaw\":\"ЛЕК Ёлка  Тест\"", "\"subjectRaw\":null"))
        expectInvalidSchedule(good.replace("\"dayOfWeek\":1", "\"dayOfWeek\":8"))
        expectInvalidSchedule(good.replace("\"parity\":0", "\"parity\":3"))
        expectInvalidSchedule(good.replace("\"index\":1", "\"index\":0"))
        expectInvalidSchedule(good.replace("\"timeStart\":\"09:00\"", "\"timeStart\":\"9:00\""))
        expectInvalidSchedule(good.replace("\"timeEnd\":\"10:35\"", "\"timeEnd\":\"00:35\""))
        expectInvalidSchedule(good.replace("\"timeEnd\":\"10:35\"", "\"timeEnd\":\"11:00\""))
        expectInvalidSchedule(good.replace(SHA, "A".repeat(64)))
        expectInvalidSchedule(good.replace("2026-09-01", "2026-02-30"))
        expectInvalidSchedule(good.replace("Europe/Moscow", "UTC"))
        expectInvalidSchedule(good.replace("\"weekCount\":2", "\"weekCount\":1"))
        expectInvalidSchedule(good.replace("2026-09-08T10:00:00+00:00", "2026-09-08T10:00:00"))
        expectInvalidSchedule(good.replace(",\"stale\":false", ""))
        val lesson = good.substringAfter("\"lessons\":[").substringBefore("]")
        expectInvalidSchedule(good.replace("\"lessons\":[$lesson]", "\"lessons\":[$lesson,$lesson]"))
    }

    @Test
    fun rejects_invalid_catalog() = runBlocking {
        val good = catalogJson()
        val groupA = """{"id":"a","name":"ТЕСТ-ГРУППА","lessonCount":1}"""
        val bodies = listOf(
            good.replace("\"groups\":[$groupA,", "\"groups\":[$groupA,$groupA,"),
            good.replace("\"id\":\"a\"", "\"id\":\"\""),
            good.replace("\"id\":\"a\"", "\"id\":\" a \""),
            good.replace("\"id\":\"a\"", "\"id\":\"${"a".repeat(65)}\""),
            good.replace("\"lessonCount\":1", "\"lessonCount\":-1")
        )
        for (body in bodies) {
            val http = FakeHttp { jsonReply(body) }
            val client = TimetableApiClient(http, URI("https://example.invalid/"))
            try {
                client.fetch(emptyList())
                fail(body.take(80))
            } catch (e: TimetableApiException) {
                assertEquals(TimetableApiFailure.InvalidPayload, e.failure)
            }
        }
    }

    @Test
    fun rejects_duplicate_object_keys() = runBlocking {
        val body = catalogJson().replaceFirst("\"stale\":false", "\"stale\":false,\"stale\":true")
        val http = FakeHttp { jsonReply(body) }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        try {
            client.fetch(emptyList())
            fail()
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.InvalidPayload, e.failure)
        }
    }
}
