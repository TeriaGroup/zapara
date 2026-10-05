package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text
import java.io.IOException
import java.net.URI
import java.time.Duration
import java.util.UUID

class PrivateSyncHttpClientTest {
    @Test
    fun five_routes_use_exact_prefix_query_headers_and_canonical_retry() = runBlocking {
        val http = FakeHttp { call ->
            when {
                call.url.endsWith("/metadata") -> jsonReply(200, metadataJson())
                call.url.endsWith("/mutations") -> jsonReply(
                    200,
                    mutationResultJson(200, "applied", record = recordJson())
                )
                call.url.contains("/changes?") -> jsonReply(200, changesPageJson())
                call.method == "POST" && call.url.endsWith("/resync") -> jsonReply(200, manifestJson())
                else -> jsonReply(200, resyncPageJson())
            }
        }
        val c = client(http)
        assertEquals(PrivateSyncState.Success, c.metadata(ACCESS).state)
        assertEquals(PrivateSyncState.Success, c.mutate(ACCESS, mutation()).state)
        assertEquals(PrivateSyncState.Success, c.mutate(ACCESS, mutation()).state)
        val changes = c.changes(ACCESS, EPOCH, 0, 1)
        assertEquals(PrivateSyncState.Success, changes.state)
        assertEquals(1L, changes.value!!.nextAfterSequence)
        assertEquals(9L, changes.value!!.metadata.currentSequence)
        assertEquals(PrivateSyncState.Success, c.beginResync(ACCESS).state)
        assertEquals(PrivateSyncState.Success, c.readResyncPage(ACCESS, manifest(), 0, 1).state)

        val urls = http.requests.map { "${it.method} ${it.url.removePrefix("https://example.test")}" }
        assertEquals(
            listOf(
                "GET /root/api/v1/sync/metadata",
                "POST /root/api/v1/sync/mutations",
                "POST /root/api/v1/sync/mutations",
                "GET /root/api/v1/sync/changes?epoch=$EPOCH&afterSequence=0&limit=1",
                "POST /root/api/v1/sync/resync",
                "GET /root/api/v1/sync/resync/$MANIFEST_ID?afterOrdinal=0&limit=1"
            ),
            urls
        )
        http.requests.forEach { call ->
            assertEquals("Bearer $ACCESS", call.headers["Authorization"])
            assertEquals("application/json", call.headers["Accept"])
            assertFalse(call.headers.keys.any { it.equals("Cookie", true) })
        }
        val bodies = http.requests.filter { it.url.endsWith("/mutations") }.map { it.body!!.copyOf() }
        assertTrue(bodies[0].contentEquals(bodies[1]))
        val sent = StrictJson.parse(bodies[0]).obj()
        assertEquals(EPOCH.toString(), sent.text("syncEpoch", 36))
        assertEquals(OP.toString(), sent.text("opId", 36))
        assertEquals("completion", sent.text("entityType", 16))
        assertEquals("upsert", sent.text("action", 16))
        assertEquals("application/json; charset=utf-8", http.requests[1].headers["Content-Type"])
        assertNull(http.requests[0].body)
        assertEquals(SyncValidation.REQUEST_BYTES, http.requests[0].maxBytes)
        assertEquals(SyncValidation.PAGE_BYTES, http.requests[3].maxBytes)
    }

    @Test
    fun mutation_outcomes_preserve_receipt_and_allow_new_reset_epoch() = runBlocking {
        val cases = listOf(
            Triple(409, "revision_conflict", PrivateSyncState.Conflict),
            Triple(409, "op_id_reused", PrivateSyncState.Conflict),
            Triple(410, "sync_reset", PrivateSyncState.ResetRequired)
        )
        for ((status, code, state) in cases) {
            val meta = if (status == 410) metadataJson(UUID.fromString("dddddddd-dddd-dddd-dddd-dddddddddddd"), 0, 0)
            else metadataJson()
            val record = if (code == "revision_conflict") recordJson() else null
            val http = FakeHttp { jsonReply(status, mutationResultJson(status, code, meta, record)) }
            val pending = mutation()
            val result = client(http).mutate(ACCESS, pending)
            assertEquals(state, result.state)
            assertNotNull(result.mutationOutcome)
            assertEquals(status, result.mutationOutcome!!.status)
            assertEquals(code, result.mutationOutcome!!.code)
            if (status == 200) assertNotNull(result.value) else assertNull(result.value)
            if (status == 410) {
                assertNotEquals(pending.syncEpoch, result.mutationOutcome!!.metadata.syncEpoch)
                assertEquals(EPOCH, pending.syncEpoch)
                assertEquals(OP, pending.opId)
            }
        }
    }

    @Test
    fun applied_receipt_must_match_request_and_http_status() = runBlocking {
        val foreignEntity = UUID.fromString("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
        val foreignEpoch = UUID.fromString("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        val cases = listOf(
            jsonReply(200, mutationResultJson(200, "applied", record = recordJson(entityId = foreignEntity))),
            jsonReply(200, mutationResultJson(200, "applied", metadataJson(foreignEpoch), recordJson())),
            jsonReply(200, mutationResultJson(409, "revision_conflict", record = recordJson())),
            jsonReply(409, mutationResultJson(200, "applied", record = recordJson())),
            jsonReply(200, mutationResultJson(200, "applied", record = recordJson(revision = 10))),
            jsonReply(
                200,
                mutationResultJson(
                    200,
                    "applied",
                    record = recordJson(tombstone = true, value = null)
                )
            )
        )
        for (reply in cases) {
            val http = FakeHttp { reply }
            assertEquals(PrivateSyncState.InvalidResponse, client(http).mutate(ACCESS, mutation()).state)
        }
    }

    @Test
    fun errors_are_typed_and_never_automatically_retried() = runBlocking {
        val cases = listOf(
            Triple(410, "sync_reset", PrivateSyncState.ResetRequired),
            Triple(401, "invalid_session", PrivateSyncState.NeedsReauthentication),
            Triple(429, "rate_limited", PrivateSyncState.RateLimited),
            Triple(503, "db_unavailable", PrivateSyncState.Unavailable),
            Triple(400, "invalid_cursor", PrivateSyncState.InvalidRequest)
        )
        for ((status, code, state) in cases) {
            var count = 0
            val http = FakeHttp {
                count++
                jsonReply(status, errorJson(status, code), headers = mapOf("Retry-After" to "604800"))
            }
            val result = client(http).changes(ACCESS, EPOCH, 0)
            assertEquals(code, state, result.state)
            assertEquals(1, count)
            if (status == 429) assertEquals(Duration.ofMinutes(5), result.retryAfter)
            else assertNull(result.retryAfter)
        }
    }

    @Test
    fun feed_cursor_is_last_returned_not_high_water_and_pages_keep_manifest() = runBlocking {
        val http = FakeHttp { call ->
            if (call.url.contains("/changes")) jsonReply(200, changesPageJson())
            else jsonReply(200, resyncPageJson())
        }
        val c = client(http)
        val changes = c.changes(ACCESS, EPOCH, 0, 1)
        assertEquals(PrivateSyncState.Success, changes.state)
        assertEquals(1L, PrivateSyncHttpClient.pullNextAfterSequence(changes.value!!))
        assertNotEquals(changes.value!!.metadata.currentSequence, PrivateSyncHttpClient.pullNextAfterSequence(changes.value!!))
        assertEquals(1L, changes.value!!.changes[0].sequence)
        val read = c.readResyncPage(ACCESS, manifest(), 0, 1)
        assertEquals(MANIFEST_ID, read.value!!.manifest.manifestId)
        assertEquals(9L, read.value!!.manifest.highWater)
        assertEquals(EPOCH, read.value!!.manifest.syncEpoch)
    }

    @Test
    fun manifest_expired_is_typed_and_problem_json_401_does_not_echo_secrets() = runBlocking {
        val expired = FakeHttp { jsonReply(410, errorJson(410, "manifest_expired")) }
        val page = client(expired).readResyncPage(ACCESS, manifest(), 0, 1)
        assertEquals(PrivateSyncState.ManifestExpired, page.state)
        assertNull(page.value)
        val auth = FakeHttp {
            jsonReply(401, errorJson(401, "invalid_session", "synthetic"), "application/problem+json")
        }
        val result = client(auth).metadata(ACCESS)
        assertEquals(PrivateSyncState.NeedsReauthentication, result.state)
        assertFalse(result.toString().contains(ACCESS))
        assertFalse(result.diagnostic.contains(ACCESS))
        assertFalse(result.toString().contains("synthetic"))
        assertFalse(result.diagnostic.contains("synthetic"))
    }

    @Test
    fun metadata_410_sync_reset_is_invalid_not_success() = runBlocking {
        val http = FakeHttp { jsonReply(410, errorJson(410, "sync_reset")) }
        assertEquals(PrivateSyncState.InvalidResponse, client(http).metadata(ACCESS).state)
    }

    @Test
    fun foreign_feed_and_manifest_fields_are_invalid() = runBlocking {
        val foreign = UUID.fromString("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        val httpEpoch = FakeHttp { jsonReply(200, changesPageJson(metadataJson(foreign))) }
        assertEquals(PrivateSyncState.InvalidResponse, client(httpEpoch).changes(ACCESS, EPOCH, 0, 1).state)
        val httpAfter = FakeHttp { jsonReply(200, changesPageJson(after = 1, next = 1, hasMore = false, changes = "[]")) }
        assertEquals(PrivateSyncState.InvalidResponse, client(httpAfter).changes(ACCESS, EPOCH, 0, 1).state)
        val httpLimit = FakeHttp {
            jsonReply(
                200,
                changesPageJson(
                    next = 2,
                    changes = """[{"sequence":1,"opId":"$OP","record":${recordJson()}},{"sequence":2,"opId":"$OP","record":${recordJson()}}]"""
                )
            )
        }
        assertEquals(PrivateSyncState.InvalidResponse, client(httpLimit).changes(ACCESS, EPOCH, 0, 1).state)
        val httpManifest = FakeHttp {
            jsonReply(200, resyncPageJson(manifestJson(id = UUID.fromString("55555555-5555-5555-5555-555555555555"))))
        }
        assertEquals(PrivateSyncState.InvalidResponse, client(httpManifest).readResyncPage(ACCESS, manifest(), 0, 1).state)
    }

    @Test
    fun guards_reject_bad_input_without_network() = runBlocking {
        var count = 0
        val http = FakeHttp {
            count++
            jsonReply(200, metadataJson())
        }
        val c = client(http)
        assertEquals(PrivateSyncState.InvalidRequest, c.metadata("invalid").state)
        assertEquals(PrivateSyncState.InvalidRequest, c.changes(ACCESS, UUID(0, 0), 0).state)
        assertEquals(PrivateSyncState.InvalidRequest, c.changes(ACCESS, EPOCH, -1).state)
        assertEquals(PrivateSyncState.InvalidRequest, c.changes(ACCESS, EPOCH, 0, 201).state)
        assertEquals(PrivateSyncState.InvalidRequest, c.readResyncPage(ACCESS, manifest(), -1).state)
        assertEquals(PrivateSyncState.InvalidRequest, c.readResyncPage(ACCESS, manifest(), 2).state)
        assertEquals(0, count)
    }

    @Test
    fun malformed_payloads_are_invalid_and_do_not_print_bodies() = runBlocking {
        val bodies = listOf(
            """{"syncEpoch":"$EPOCH","currentSequence":0}""",
            """{"syncEpoch":"$EPOCH","currentSequence":0,"minAfterSequence":0,"secret":1}""",
            """{"syncEpoch":"$EPOCH","currentSequence":0,"minAfterSequence":0,"minAfterSequence":0}""",
            "not json"
        )
        for (body in bodies) {
            val http = FakeHttp { jsonReply(200, body) }
            val result = client(http).metadata(ACCESS)
            assertEquals(PrivateSyncState.InvalidResponse, result.state)
            assertFalse(result.toString().contains(ACCESS))
            assertFalse(result.diagnostic.contains("secret"))
        }
        val utf8 = FakeHttp { rawReply(200, byteArrayOf(0xff.toByte(), 0xfe.toByte())) }
        assertEquals(PrivateSyncState.InvalidResponse, client(utf8).metadata(ACCESS).state)
        val depth = FakeHttp { jsonReply(200, "[".repeat(17) + "]".repeat(17)) }
        assertEquals(PrivateSyncState.InvalidResponse, client(depth).metadata(ACCESS).state)
    }

    @Test
    fun bodies_are_capped_and_page_bound_is_eight_mebibytes() = runBlocking {
        val overMeta = FakeHttp { rawReply(200, ByteArray(65537)) }
        assertEquals(PrivateSyncState.InvalidResponse, client(overMeta).metadata(ACCESS).state)
        val overError = FakeHttp { rawReply(400, ByteArray(4097), headers = mapOf()) }
        assertEquals(PrivateSyncState.InvalidResponse, client(overError).metadata(ACCESS).state)
        val overPage = FakeHttp { rawReply(200, ByteArray(8 * 1024 * 1024 + 1)) }
        assertEquals(PrivateSyncState.InvalidResponse, client(overPage).changes(ACCESS, EPOCH, 0).state)
        val http = FakeHttp { jsonReply(200, metadataJson()) }
        client(http).metadata(ACCESS)
        assertEquals(SyncValidation.REQUEST_BYTES, http.requests.single().maxBytes)
        val pageHttp = FakeHttp { jsonReply(200, changesPageJson(hasMore = false, next = 1)) }
        client(pageHttp).changes(ACCESS, EPOCH, 0)
        assertEquals(SyncValidation.PAGE_BYTES, pageHttp.requests.single().maxBytes)
    }

    @Test
    fun redirect_empty_or_non_json_success_is_invalid_without_retry() = runBlocking {
        for (status in listOf(302, 307, 204)) {
            var count = 0
            val http = FakeHttp {
                count++
                rawReply(status, ByteArray(0), null, mapOf("Location" to "https://other.invalid/$ACCESS"))
            }
            val result = client(http).mutate(ACCESS, mutation())
            assertEquals(PrivateSyncState.InvalidResponse, result.state)
            assertEquals(1, count)
            assertFalse(result.toString().contains(ACCESS))
        }
    }

    @Test
    fun non_json_error_and_gzip_are_invalid() = runBlocking {
        val plain = FakeHttp {
            jsonReply(401, errorJson(401, "invalid_session"), "text/plain")
        }
        assertEquals(PrivateSyncState.InvalidResponse, client(plain).metadata(ACCESS).state)
        val charset = FakeHttp {
            jsonReply(401, errorJson(401, "invalid_session"), "application/json; charset=utf-16")
        }
        assertEquals(PrivateSyncState.InvalidResponse, client(charset).metadata(ACCESS).state)
        val gzip = FakeHttp {
            jsonReply(200, metadataJson(), headers = mapOf("Content-Encoding" to "gzip"))
        }
        assertEquals(PrivateSyncState.InvalidResponse, client(gzip).metadata(ACCESS).state)
    }

    @Test
    fun transport_failure_is_unavailable_without_post_retry_or_token_echo() = runBlocking {
        var count = 0
        val http = FakeHttp {
            count++
            throw IOException(ACCESS)
        }
        val result = client(http).mutate(ACCESS, mutation())
        assertEquals(PrivateSyncState.Unavailable, result.state)
        assertEquals(1, count)
        assertFalse(result.toString().contains(ACCESS))
        assertFalse(result.diagnostic.contains(ACCESS))
    }

    @Test
    fun timeout_and_cancellation_are_typed_without_retry() = runBlocking {
        var timed = 0
        val slow = FakeHttp {
            timed++
            delay(5_000)
            jsonReply(200, metadataJson())
        }
        assertEquals(PrivateSyncState.TimedOut, client(slow, timeoutMs = 40).beginResync(ACCESS).state)
        assertEquals(1, timed)
        var cancelled = 0
        val cancel = FakeHttp {
            cancelled++
            throw CancellationException("caller")
        }
        assertEquals(PrivateSyncState.Cancelled, client(cancel).metadata(ACCESS).state)
        assertEquals(1, cancelled)
    }

    @Test
    fun invalid_base_is_rejected_without_echo() {
        for (uri in listOf(
            "http://example.test/",
            "https://user:password@example.test/",
            "https://example.test/?secret=x",
            "https://example.test/#x"
        )) {
            try {
                PrivateSyncHttpClient(FakeHttp { error("network") }, URI(uri))
                fail(uri)
            } catch (e: IllegalArgumentException) {
                assertFalse(e.toString().contains(uri))
                assertFalse(e.toString().contains("password"))
                assertFalse(e.toString().contains("secret"))
            }
        }
    }

    @Test
    fun scope_matches_account_server_scope() {
        for (url in listOf(
            "http://localhost:1234/prefix",
            "http://127.0.0.2:1234/prefix",
            "http://[::1]:1234/prefix",
            "https://example.test/prefix"
        )) {
            val scope = AccountServerScope.parse(url)
            val c = PrivateSyncHttpClient(FakeHttp { error("network") }, URI(url))
            assertEquals(scope.key, c.scope.key)
            assertEquals(scope.baseUri, c.scope.baseUri)
        }
    }
}
