package ru.bgtu_voenmeh.zapara.data.accounts

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.time.Instant

class AccountHttpClientLifecycleTest {
    private val root = "https://example.invalid/root/"
    private val family = "11111111-1111-4111-8111-111111111111"
    private val device = "22222222-2222-4222-8222-222222222222"
    private val exportId = "33333333-3333-4333-8333-333333333333"
    private val txId = "44444444-4444-4444-8444-444444444444"
    private val cursor = "A".repeat(55)
    private val created = "2026-09-01T00:00:00Z"
    private val seen = "2026-09-08T12:00:00Z"
    private val expires = "2026-10-08T00:00:00Z"

    @Test
    fun devices_revoke_and_password_use_exact_paths_and_bearer() = runBlocking {
        val access = testToken("za_", 1)
        val http = FakeHttp { call ->
            when (call.method to call.url.removePrefix(root + "api/v1/")) {
                "GET" to "account/devices?limit=20" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    assertNull(call.body)
                    json(
                        """{"devices":[{"familyId":"$family","deviceId":"$device","deviceName":"Pixel",
                        |"platform":"android","createdAt":"$created","lastSeenAt":"$seen",
                        |"expiresAt":"$expires","isCurrent":true}],"nextCursor":"$cursor"}""".trimMargin()
                    )
                }
                "GET" to "account/devices?limit=100&cursor=$cursor" -> json(
                    """{"devices":[],"nextCursor":null}"""
                )
                "DELETE" to "account/devices/$family" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    assertNull(call.body)
                    HttpReply(204, ByteArray(0))
                }
                "POST" to "account/sessions/revoke-all" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    assertNull(call.body)
                    HttpReply(204, ByteArray(0))
                }
                "POST" to "account/password/change" -> {
                    assertEquals("""{"currentPassword":"password12ab","newPassword":"password12cd"}""", String(call.body!!))
                    HttpReply(204, ByteArray(0))
                }
                else -> error(call.method + " " + call.url)
            }
        }
        val client = httpClient(http)
        val page = client.listDevices(access)
        assertEquals(family, page.devices.single().familyId)
        assertEquals(device, page.devices.single().deviceId)
        assertEquals("Pixel", page.devices.single().deviceName)
        assertEquals("android", page.devices.single().platform)
        assertTrue(page.devices.single().isCurrent)
        assertEquals(cursor, page.nextCursor)
        assertEquals(0, client.listDevices(access, 100, cursor).devices.size)
        client.revokeSession(access, family)
        client.revokeAll(access)
        client.changePassword(access, "password12ab", "password12cd")
        assertEquals(5, http.requests.size)
        assertEquals(0, http.requests.count { it.url.contains("id.vk") || it.url.contains("yandex") })
    }

    @Test
    fun listDevices_rejects_limit_and_cursor_without_network() = runBlocking {
        val http = FakeHttp { error("network") }
        val client = httpClient(http)
        val access = testToken("za_", 1)
        for (limit in listOf(0, 101, -1)) {
            try {
                client.listDevices(access, limit)
                fail("limit $limit")
            } catch (e: AccountClientException) {
                assertEquals(AccountClientFailure.InvalidRequest, e.failure)
            }
        }
        try {
            client.listDevices(access, cursor = "?invalid")
            fail()
        } catch (e: AccountClientException) {
            assertEquals(AccountClientFailure.InvalidRequest, e.failure)
        }
        assertEquals(0, http.requests.size)
    }

    @Test
    fun export_download_delete_require_proof_and_keep_download_authenticated() = runBlocking {
        val access = testToken("za_", 1)
        val proof = opaque(7)
        val payload = """{"profile":{"userId":"$family"},"devices":[]}""".toByteArray()
        val http = FakeHttp { call ->
            val path = call.url.removePrefix(root + "api/v1/")
            when {
                call.method == "POST" && path == "account/exports" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    assertEquals("""{"proofToken":"$proof"}""", String(call.body!!))
                    json(exportJson(), 202)
                }
                call.method == "GET" && path == "account/exports/$exportId" -> json(exportJson())
                call.method == "GET" && path == "account/exports/$exportId/download" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    assertTrue(call.maxBytes > 65536)
                    HttpReply(
                        200,
                        payload,
                        "application/json; charset=utf-8",
                        mapOf("Content-Disposition" to "attachment; filename=\"zapara-export-$exportId.json\"")
                    )
                }
                call.method == "DELETE" && path == "account" -> {
                    assertEquals("""{"proofToken":"$proof"}""", String(call.body!!))
                    json("""{"status":"deleting","remoteWipe":false}""", 202)
                }
                else -> error(path)
            }
        }
        val client = httpClient(http)
        val created = client.createExport(access, proof)
        assertEquals(exportId, created.exportId)
        assertEquals("ready", created.status)
        assertEquals("ready", client.getExport(access, exportId).status)
        val file = client.downloadExport(access, exportId)
        assertEquals("zapara-export-$exportId.json", file.fileName)
        assertTrue(file.bytes.contentEquals(payload))
        assertTrue(!file.toString().contains(String(payload)))
        val deleted = client.deleteAccount(access, proof)
        assertEquals("deleting", deleted.status)
        assertFalse(deleted.remoteWipe)
    }

    @Test
    fun export_maps_invalid_proof_401_and_export_not_found() = runBlocking {
        val access = testToken("za_", 1)
        val http = FakeHttp { call ->
            when (call.url.substringAfter("/api/v1/")) {
                "account/exports" -> problem(403, "invalid_external_proof")
                "account/exports/$exportId" -> problem(404, "export_not_found")
                else -> problem(401, "invalid_session")
            }
        }
        val client = httpClient(http)
        assertFailure(AccountClientFailure.InvalidExternalProof) { client.createExport(access, opaque(8)) }
        assertFailure(AccountClientFailure.ExportNotFound) { client.getExport(access, exportId) }
        assertFailure(AccountClientFailure.InvalidSession) { client.downloadExport(access, exportId) }
        val secret = problem(403, "invalid_external_proof", title = "canary-secret")
        val titled = FakeHttp { secret }
        try {
            httpClient(titled).createExport(access, opaque(9))
            fail()
        } catch (e: AccountClientException) {
            assertEquals(AccountClientFailure.InvalidExternalProof, e.failure)
            assertEquals("Операция аккаунта не выполнена.", e.message)
            assertTrue(!e.toString().contains("canary-secret"))
        }
    }

    @Test
    fun download_rejects_non_json_content_type() = runBlocking {
        val http = FakeHttp {
            HttpReply(200, "{}".toByteArray(), "text/html", mapOf("Content-Disposition" to "attachment; filename=x.json"))
        }
        assertFailure(AccountClientFailure.InvalidPayload) {
            httpClient(http).downloadExport(testToken("za_", 1), exportId)
        }
    }

    @Test
    fun recovery_request_is_202_empty_for_any_username_and_sends_no_bearer() = runBlocking {
        val bodies = mutableListOf<String>()
        val http = FakeHttp { call ->
            assertEquals("POST", call.method)
            assertEquals(root + "api/v1/auth/password-reset/request", call.url)
            assertNull(call.headers["Authorization"])
            bodies += String(call.body!!)
            json("{}", 202)
        }
        val client = httpClient(http)
        client.requestPasswordReset("no_such_user")
        client.requestPasswordReset("synthetic")
        assertEquals("""{"username":"no_such_user"}""", bodies[0])
        assertEquals("""{"username":"synthetic"}""", bodies[1])
        val confirm = FakeHttp { call ->
            assertEquals(root + "api/v1/auth/password-reset/confirm", call.url)
            assertEquals("""{"token":"${opaque(3)}","newPassword":"password12zz"}""", String(call.body!!))
            HttpReply(204, ByteArray(0))
        }
        httpClient(confirm).confirmPasswordReset(opaque(3), "password12zz")
    }

    @Test
    fun json_request_cap_is_16kib_without_network() = runBlocking {
        val http = FakeHttp { error("network") }
        try {
            httpClient(http).requestPasswordReset("u".repeat(20000))
            fail()
        } catch (e: AccountClientException) {
            assertEquals(AccountClientFailure.InvalidRequest, e.failure)
        }
        assertEquals(0, http.requests.size)
    }

    @Test
    fun reauthenticate_and_external_mock_paths_reject_other_providers() = runBlocking {
        val access = testToken("za_", 1)
        val challenge = opaque(4)
        val verifier = opaque(5)
        val handoff = opaque(6)
        val proof = opaque(7)
        val sessionJson = sessionJson()
        val http = FakeHttp { call ->
            val path = call.url.removePrefix(root + "api/v1/")
            when {
                path == "account/reauthenticate" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    assertTrue(String(call.body!!).contains("\"purpose\":\"export\""))
                    json("""{"proofToken":"$proof","purpose":"export","expiresAt":"$seen"}""")
                }
                path == "auth/external/yandex/start" -> {
                    assertTrue(String(call.body!!).contains("\"nativeChallengeMethod\":\"S256\""))
                    assertTrue(String(call.body!!).contains("\"platform\":\"android\""))
                    assertTrue(!call.url.contains("oauth.yandex") && !call.url.contains("id.vk"))
                    json("""{"transactionId":"$txId","authorizeUrl":"https://example.invalid/mock/authorize","expiresAt":"$seen"}""")
                }
                path == "auth/external/vk/start" -> problem(503, "provider_unavailable")
                path == "auth/external/exchange" -> json(
                    """{"status":"completed","session":$sessionJson,"proof":null}"""
                )
                path == "auth/external/$txId/status" -> json("""{"status":"awaitingApp"}""")
                else -> error(path)
            }
        }
        val client = httpClient(http)
        val reauth = client.reauthenticate(access, "password12ab", "export")
        assertEquals(proof, reauth.proofToken)
        assertEquals("export", reauth.purpose)
        assertTrue(!reauth.toString().contains(proof))
        val start = client.externalStart(
            "yandex",
            AccountExternalStartRequest(
                purpose = "login",
                nativeChallenge = challenge,
                nativeChallengeMethod = "S256",
                deviceId = device,
                deviceName = "Pixel",
                platform = "android",
                returnKind = "android"
            )
        )
        assertEquals(txId, start.transactionId)
        assertTrue(!start.toString().contains("authorize"))
        assertFailure(AccountClientFailure.ProviderUnavailable) {
            client.externalStart(
                "vk",
                AccountExternalStartRequest(
                    "login", challenge, "S256", device, "Pixel", "android", "android"
                )
            )
        }
        val exchanged = client.externalExchange(AccountExternalExchangeRequest(txId, verifier, handoff))
        assertEquals("completed", exchanged.status)
        assertEquals("Test.User", exchanged.session!!.user.username)
        assertNull(exchanged.proof)
        assertEquals("awaitingApp", client.externalStatus(txId).status)
        val blocked = FakeHttp { error("live-idp") }
        for (provider in listOf("google", "vk/../yandex", "VK", "yandex.com")) {
            try {
                httpClient(blocked).externalStart(
                    provider,
                    AccountExternalStartRequest("login", challenge, "S256", device, "Pixel", "android", "android")
                )
                fail(provider)
            } catch (e: AccountClientException) {
                assertEquals(AccountClientFailure.InvalidRequest, e.failure)
            }
        }
        assertEquals(0, blocked.requests.size)
    }

    @Test
    fun identities_and_unlink_use_vk_or_yandex_only() = runBlocking {
        val access = testToken("za_", 1)
        val proof = opaque(2)
        val http = FakeHttp { call ->
            val path = call.url.removePrefix(root + "api/v1/")
            when (call.method to path) {
                "GET" to "account/identities" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    json("""[{"provider":"yandex","linkedAt":"$seen"}]""")
                }
                "DELETE" to "account/identities/yandex" -> {
                    assertEquals("""{"proofToken":"$proof"}""", String(call.body!!))
                    HttpReply(204, ByteArray(0))
                }
                else -> error(path)
            }
        }
        val client = httpClient(http)
        val row = client.identities(access).single()
        assertEquals("yandex", row.provider)
        assertEquals(Instant.parse(seen), row.linkedAt)
        client.unlinkIdentity(access, "yandex", proof)
        val blocked = FakeHttp { error("network") }
        try {
            httpClient(blocked).unlinkIdentity(access, "google", proof)
            fail()
        } catch (e: AccountClientException) {
            assertEquals(AccountClientFailure.InvalidRequest, e.failure)
        }
        assertEquals(0, blocked.requests.size)
    }

    @Test
    fun recovery_unavailable_and_expired_external_are_mapped() = runBlocking {
        val access = testToken("za_", 1)
        assertFailure(AccountClientFailure.RecoveryUnavailable) {
            httpClient(FakeHttp { problem(503, "recovery_unavailable") }).requestPasswordReset("synthetic")
        }
        assertFailure(AccountClientFailure.ExternalAttemptExpired) {
            httpClient(FakeHttp { problem(410, "external_attempt_expired") }).externalStatus(txId)
        }
        assertFailure(AccountClientFailure.LastLoginMethod) {
            httpClient(FakeHttp { problem(409, "last_login_method") }).unlinkIdentity(access, "vk", opaque(1))
        }
        assertFailure(AccountClientFailure.IdentityUnavailable) {
            httpClient(FakeHttp { problem(409, "identity_unavailable") }).unlinkIdentity(access, "vk", opaque(1))
        }
        assertFailure(AccountClientFailure.PasswordAlreadySet) {
            httpClient(FakeHttp { problem(409, "password_already_set") }).reauthenticate(access, "password12ab", "set_password")
        }
    }

    private fun httpClient(http: FakeHttp) = AccountHttpClient(http, AccountServerScope.parse("https://example.invalid/root"))

    private fun json(body: String, status: Int = 200) = HttpReply(status, body.toByteArray(), "application/json")

    private fun problem(status: Int, code: String, title: String = "x") =
        HttpReply(status, """{"title":"$title","status":$status,"code":"$code"}""".toByteArray())

    private fun exportJson() =
        """{"exportId":"$exportId","status":"ready","createdAt":"$seen","completedAt":"$seen","expiresAt":"$expires"}"""

    private fun sessionJson(): String {
        val access = testToken("za_", 11)
        val refresh = testToken("zr_", 12)
        return """{"user":{"userId":"$family","username":"Test.User","displayName":null,"createdAt":"$created"},
            |"familyId":"$family","accessToken":"$access","refreshToken":"$refresh","tokenType":"Bearer",
            |"accessExpiresAt":"$seen","refreshExpiresAt":"$expires"}""".trimMargin()
    }

    private fun opaque(fill: Int): String =
        java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { fill.toByte() })

    private suspend fun assertFailure(failure: AccountClientFailure, block: suspend () -> Unit) {
        try {
            block()
            fail(failure.name)
        } catch (e: AccountClientException) {
            assertEquals(failure, e.failure)
        }
    }
}
