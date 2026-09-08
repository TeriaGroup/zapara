package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.net.URI

class TimetableApiClientTest {
    @Test
    fun fetches_pinned_distinct_groups_including_explicit_empty_and_preserves_raw() = runBlocking {
        val http = FakeHttp { call ->
            jsonReply(if (call.url.endsWith("/groups")) catalogJson() else scheduleJson(if (call.url.contains("/empty/")) "empty" else "a"))
        }
        val client = TimetableApiClient(http, URI("https://example.invalid/prefix"))
        val result = client.fetch(listOf("empty", "a", "a"))
        assertEquals(2, result.groups.size)
        assertEquals(2, result.downloaded.size)
        assertTrue(result.downloaded.getValue("empty").lessons.isEmpty())
        val row = result.downloaded.getValue("a").lessons.single()
        assertEquals("ЛЕК Ёлка  Тест", row.subjectRaw)
        assertEquals("493*", row.toLesson("a").classroomRaw)
        assertEquals("ТЕСТ-ГРУППА", result.groups[0].name)
        assertEquals("https://example.invalid/prefix/api/v1/groups", http.requests[0].url)
        val rest = http.requests.drop(1).map { it.url }.sorted()
        assertEquals(
            listOf(
                "https://example.invalid/prefix/api/v1/groups/a/timetable?snapshotId=$PIN",
                "https://example.invalid/prefix/api/v1/groups/empty/timetable?snapshotId=$PIN"
            ),
            rest
        )
    }

    @Test
    fun catalog_only_leaves_all_groups_unfetched() = runBlocking {
        val http = FakeHttp { jsonReply(catalogJson()) }
        val client = TimetableApiClient(http, URI("http://[::1]:1234/"))
        val result = client.fetch(emptyList())
        assertEquals(2, result.groups.size)
        assertTrue(result.downloaded.isEmpty())
        assertEquals(1, http.requests.size)
    }

    @Test
    fun unknown_required_group_fails_before_schedule_download() = runBlocking {
        val http = FakeHttp { jsonReply(catalogJson()) }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        try {
            client.fetch(listOf("absent"))
            fail()
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.UnknownRequiredGroup, e.failure)
        }
        assertEquals(1, http.requests.size)
    }

    @Test
    fun missing_generation_retries_entire_catalog_only_once() = runBlocking {
        var catalogs = 0
        val http = FakeHttp { call ->
            if (call.url.substringBefore('?').endsWith("/groups")) {
                catalogs++
                jsonReply(catalogJson(if (catalogs == 1) PIN else NEW_PIN))
            } else if (catalogs == 1) missingPin()
            else jsonReply(scheduleJson(pin = NEW_PIN))
        }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        val result = client.fetch(listOf("a"))
        assertEquals(NEW_PIN, result.downloaded.getValue("a").meta.snapshotId)
        assertEquals(2, catalogs)
    }

    @Test
    fun always_missing_generation_fails_after_one_retry() = runBlocking {
        var catalogs = 0
        val http = FakeHttp { call ->
            if (call.url.substringBefore('?').endsWith("/groups")) {
                catalogs++
                jsonReply(catalogJson())
            } else missingPin()
        }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        try {
            client.fetch(listOf("a"))
            fail()
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.SnapshotUnavailable, e.failure)
        }
        assertEquals(2, catalogs)
    }

    @Test
    fun rejects_unsafe_base_uri() {
        val http = FakeHttp { jsonReply("{}") }
        for (uri in listOf(
            "http://example.invalid/",
            "https://user:secret@example.invalid/",
            "https://example.invalid/?token=secret",
            "https://example.invalid/#secret",
            "file:///tmp/"
        )) {
            try {
                TimetableApiClient(http, URI(uri))
                fail(uri)
            } catch (_: IllegalArgumentException) {
            }
        }
    }

    @Test
    fun other_errors_never_retry_or_return_partial_output() = runBlocking {
        data class Case(val status: Int, val body: String)
        for (case in listOf(
            Case(503, """{"code":"snapshot_not_found","status":503}"""),
            Case(404, """{"code":"group_not_found","status":404}"""),
            Case(404, """{"code":"snapshot_not_found","status":503}"""),
            Case(404, "<html>secret</html>")
        )) {
            var catalogs = 0
            val http = FakeHttp { call ->
                if (call.url.substringBefore('?').endsWith("/groups")) {
                    catalogs++
                    jsonReply(catalogJson())
                } else HttpReply(case.status, case.body.toByteArray())
            }
            val client = TimetableApiClient(http, URI("https://example.invalid/"))
            try {
                client.fetch(listOf("a"))
                fail(case.body)
            } catch (e: TimetableApiException) {
                assertEquals(1, catalogs)
                assertTrue(!e.message.orEmpty().contains("secret"))
                assertTrue(!e.toString().contains("example.invalid"))
            }
        }
    }

    @Test
    fun rejects_json_to_xml_roundtrip_payload() = runBlocking {
        val http = FakeHttp { HttpReply(200, "<Timetable/>".toByteArray()) }
        val client = TimetableApiClient(http, URI("https://example.invalid/"))
        try {
            client.fetch(emptyList())
            fail()
        } catch (e: TimetableApiException) {
            assertEquals(TimetableApiFailure.InvalidPayload, e.failure)
        }
    }

    @Test
    fun loopback_http_is_allowed() {
        TimetableApiClient(FakeHttp { jsonReply("{}") }, URI("http://127.0.0.1:5187/"))
        TimetableApiClient(FakeHttp { jsonReply("{}") }, URI("http://localhost/"))
    }
}
