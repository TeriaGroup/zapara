package ru.bgtu_voenmeh.zapara.data.accounts

import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.arr
import ru.bgtu_voenmeh.zapara.data.api.array
import ru.bgtu_voenmeh.zapara.data.api.bool
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.nullableText
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text
import java.net.URLEncoder
import java.time.Instant
import java.time.OffsetDateTime
import java.util.UUID

class AccountHttpClient(
    private val transport: HttpExchange,
    val scope: AccountServerScope
) {
    data class Capabilities(val registration: Boolean)

    suspend fun capabilities(): Capabilities {
        val root = send("GET", "auth/capabilities", null, null, 200).obj()
        return Capabilities(root.field("registration").let { (it as? JsonValue.Bool)?.value ?: false })
    }

    suspend fun register(username: String, password: String, displayName: String?): AccountUser {
        val body = buildString {
            append("{\"username\":").append(q(AccountValidation.username(username)))
            append(",\"password\":").append(q(AccountValidation.password(password)))
            append(",\"displayName\":")
            if (displayName.isNullOrBlank()) append("null") else append(q(AccountValidation.displayName(displayName)!!))
            append('}')
        }
        return user(send("POST", "auth/register", body, null, 201).obj())
    }

    suspend fun login(username: String, password: String, deviceId: String, deviceName: String): AccountSession {
        val body = buildString {
            append("{\"username\":").append(q(AccountValidation.username(username)))
            append(",\"password\":").append(q(AccountValidation.password(password)))
            append(",\"device\":{\"deviceId\":").append(q(AccountValidation.id(deviceId)))
            append(",\"deviceName\":").append(q(AccountValidation.deviceName(deviceName)))
            append(",\"platform\":\"android\"}}")
        }
        return session(send("POST", "auth/login", body, null, 200).obj())
    }

    suspend fun logout(accessToken: String) {
        send("POST", "auth/logout", "{}", accessToken, 204)
    }

    suspend fun me(accessToken: String): AccountUser {
        val root = send("GET", "account/me", null, accessToken, 200).obj()
        return user(root.field("user").obj())
    }

    suspend fun listDevices(accessToken: String, limit: Int = 20, cursor: String? = null): AccountDevicesPage {
        if (limit !in 1..100 || (cursor != null && !AccountValidation.asciiCursor(cursor))) {
            throw AccountClientException(AccountClientFailure.InvalidRequest)
        }
        val path = buildString {
            append("account/devices?limit=").append(limit)
            if (cursor != null) append("&cursor=").append(URLEncoder.encode(cursor, Charsets.UTF_8.name()))
        }
        return readPayload {
            val root = send("GET", path, null, accessToken, 200).obj()
            val items = root.array("devices", 100).items.map { device(it.obj()) }
            if (items.map { it.familyId }.toSet().size != items.size) throw JsonFail()
            val next = root.nullableText("nextCursor", 55)
            if (next != null && !AccountValidation.asciiCursor(next)) throw JsonFail()
            AccountDevicesPage(items, next)
        }
    }

    suspend fun revokeSession(accessToken: String, familyId: String) {
        send("DELETE", "account/devices/${AccountValidation.id(familyId)}", null, accessToken, 204)
    }

    suspend fun revokeAll(accessToken: String) {
        send("POST", "account/sessions/revoke-all", null, accessToken, 204)
    }

    suspend fun changePassword(accessToken: String, currentPassword: String, newPassword: String) {
        val body = buildString {
            append("{\"currentPassword\":").append(q(AccountValidation.password(currentPassword)))
            append(",\"newPassword\":").append(q(AccountValidation.password(newPassword)))
            append('}')
        }
        send("POST", "account/password/change", body, accessToken, 204)
    }

    suspend fun createExport(accessToken: String, proofToken: String): AccountExportJob = readPayload {
        exportJob(send("POST", "account/exports", proofBody(proofToken), accessToken, 202).obj())
    }

    suspend fun getExport(accessToken: String, exportId: String): AccountExportJob = readPayload {
        exportJob(send("GET", "account/exports/${AccountValidation.id(exportId)}", null, accessToken, 200).obj())
    }

    suspend fun downloadExport(accessToken: String, exportId: String): AccountExportDownload {
        val id = AccountValidation.id(exportId)
        val reply = exchange("GET", "account/exports/$id/download", null, accessToken, DOWNLOAD_MAX)
        if (reply.status != 200) throw mapError(reply.status, reply.body)
        val media = reply.contentType?.substringBefore(';')?.trim()?.lowercase()
        if (media != "application/json") throw AccountClientException(AccountClientFailure.InvalidPayload)
        return AccountExportDownload(reply.body, fileName(reply.headers, id))
    }

    suspend fun deleteAccount(accessToken: String, proofToken: String): AccountDeleteResponse {
        return readPayload {
            val root = send("DELETE", "account", proofBody(proofToken), accessToken, 202).obj()
            AccountDeleteResponse(root.text("status", 32, true), root.bool("remoteWipe"))
        }
    }

    suspend fun requestPasswordReset(username: String) {
        val body = "{\"username\":${q(username)}}"
        val root = readPayload { send("POST", "auth/password-reset/request", body, null, 202).obj() }
        if (root.fields.isNotEmpty()) throw AccountClientException(AccountClientFailure.InvalidPayload)
    }

    suspend fun confirmPasswordReset(token: String, newPassword: String) {
        val body = buildString {
            append("{\"token\":").append(q(AccountValidation.opaque(token, 43, 128)))
            append(",\"newPassword\":").append(q(AccountValidation.password(newPassword)))
            append('}')
        }
        send("POST", "auth/password-reset/confirm", body, null, 204)
    }

    suspend fun reauthenticate(accessToken: String, currentPassword: String, purpose: String): AccountReauthProof {
        if (purpose.isBlank() || purpose.length > 64) throw AccountClientException(AccountClientFailure.InvalidRequest)
        val body = buildString {
            append("{\"currentPassword\":").append(q(AccountValidation.password(currentPassword)))
            append(",\"purpose\":").append(q(purpose))
            append('}')
        }
        return readPayload { proof(send("POST", "account/reauthenticate", body, accessToken, 200).obj()) }
    }

    suspend fun externalStart(
        provider: String,
        request: AccountExternalStartRequest,
        accessToken: String? = null
    ): AccountExternalStart {
        val path = "auth/external/${requireProvider(provider)}/start"
        return readPayload {
            val root = send("POST", path, startBody(request), accessToken, 200).obj()
            AccountExternalStart(
                AccountValidation.id(root.text("transactionId", 36)),
                root.text("authorizeUrl", 2048, true),
                instant(root.text("expiresAt", 40))
            )
        }
    }

    suspend fun externalExchange(
        request: AccountExternalExchangeRequest,
        accessToken: String? = null
    ): AccountExternalExchange {
        val body = buildString {
            append("{\"transactionId\":").append(q(AccountValidation.id(request.transactionId)))
            append(",\"nativeVerifier\":").append(q(AccountValidation.opaque(request.nativeVerifier, 43, 128)))
            append(",\"handoffCode\":").append(q(AccountValidation.opaque(request.handoffCode, 43, 43)))
            append('}')
        }
        return readPayload {
            val root = send("POST", "auth/external/exchange", body, accessToken, 200).obj()
            AccountExternalExchange(
                root.text("status", 32, true),
                optionalObj(root, "session")?.let { session(it) },
                optionalObj(root, "proof")?.let { proof(it) }
            )
        }
    }

    suspend fun externalStatus(transactionId: String): AccountExternalStatus {
        val path = "auth/external/${AccountValidation.id(transactionId)}/status"
        return readPayload {
            AccountExternalStatus(send("GET", path, null, null, 200).obj().text("status", 32, true))
        }
    }

    suspend fun identities(accessToken: String): List<AccountExternalIdentity> {
        return readPayload {
            send("GET", "account/identities", null, accessToken, 200).arr().items.map { item ->
                val obj = item.obj()
                val provider = obj.text("provider", 16)
                if (provider != "vk" && provider != "yandex") throw JsonFail()
                AccountExternalIdentity(provider, instant(obj.text("linkedAt", 40)))
            }
        }
    }

    suspend fun unlinkIdentity(accessToken: String, provider: String, proofToken: String) {
        send("DELETE", "account/identities/${requireProvider(provider)}", proofBody(proofToken), accessToken, 204)
    }

    private suspend fun send(method: String, path: String, body: String?, access: String?, expected: Int): JsonValue {
        val reply = exchange(method, path, body, access, JSON_MAX)
        if (reply.status != expected) throw mapError(reply.status, reply.body)
        if (expected == 204) return JsonValue.Null
        return try {
            StrictJson.parse(reply.body)
        } catch (_: JsonFail) {
            throw AccountClientException(AccountClientFailure.InvalidPayload)
        }
    }

    private suspend fun exchange(
        method: String,
        path: String,
        body: String?,
        access: String?,
        maxBytes: Int
    ): HttpReply {
        val headers = linkedMapOf("Accept" to "application/json")
        if (access != null) headers["Authorization"] = "Bearer ${AccountValidation.token(access, "za_")}"
        val bytes = body?.toByteArray(Charsets.UTF_8)
        if (bytes != null && bytes.size > JSON_REQUEST_MAX) throw AccountClientException(AccountClientFailure.InvalidRequest)
        val reply = try {
            transport.exchange(
                HttpCall(method, scope.baseUri.toString() + "api/v1/" + path, headers, bytes, maxBytes = maxBytes)
            )
        } catch (_: ru.bgtu_voenmeh.zapara.data.api.HttpBodyTooLargeException) {
            throw AccountClientException(AccountClientFailure.BodyTooLarge)
        } catch (_: java.io.IOException) {
            throw AccountClientException(AccountClientFailure.Transport)
        }
        if (reply.body.size > maxBytes) throw AccountClientException(AccountClientFailure.BodyTooLarge)
        return reply
    }

    private fun mapError(status: Int, body: ByteArray): AccountClientException {
        val code = try {
            StrictJson.parse(body).obj().text("code", 64)
        } catch (_: Exception) {
            null
        }
        val failure = when (status to code) {
            400 to "invalid_request", 413 to "invalid_request", 415 to "invalid_request" -> AccountClientFailure.InvalidRequest
            401 to "invalid_credentials" -> AccountClientFailure.InvalidCredentials
            401 to "invalid_session" -> AccountClientFailure.InvalidSession
            403 to "invalid_external_proof" -> AccountClientFailure.InvalidExternalProof
            409 to "username_unavailable" -> AccountClientFailure.UsernameUnavailable
            409 to "last_login_method" -> AccountClientFailure.LastLoginMethod
            409 to "identity_unavailable" -> AccountClientFailure.IdentityUnavailable
            409 to "password_already_set" -> AccountClientFailure.PasswordAlreadySet
            404 to "session_not_found" -> AccountClientFailure.SessionNotFound
            404 to "export_not_found" -> AccountClientFailure.ExportNotFound
            410 to "external_attempt_expired" -> AccountClientFailure.ExternalAttemptExpired
            429 to "rate_limited" -> AccountClientFailure.RateLimited
            503 to "db_unavailable" -> AccountClientFailure.DbUnavailable
            503 to "registration_unavailable" -> AccountClientFailure.RegistrationUnavailable
            503 to "provider_unavailable" -> AccountClientFailure.ProviderUnavailable
            503 to "recovery_unavailable" -> AccountClientFailure.RecoveryUnavailable
            500 to "internal_error" -> AccountClientFailure.InternalError
            else -> if (status == 404) AccountClientFailure.NotConfigured else AccountClientFailure.ServerUnavailable
        }
        return AccountClientException(failure)
    }

    private fun user(obj: JsonValue.Obj) = AccountUser(
        AccountValidation.id(obj.text("userId", 36)),
        AccountValidation.username(obj.text("username", 32)),
        obj.nullableText("displayName", 80),
        instant(obj.text("createdAt", 40))
    )

    private fun session(obj: JsonValue.Obj) = AccountSession(
        user = user(obj.field("user").obj()),
        familyId = AccountValidation.id(obj.text("familyId", 36)),
        accessToken = AccountValidation.token(obj.text("accessToken", 46), "za_"),
        refreshToken = AccountValidation.token(obj.text("refreshToken", 46), "zr_"),
        tokenType = obj.text("tokenType", 16).also { if (it != "Bearer") throw AccountClientException(AccountClientFailure.InvalidPayload) },
        accessExpiresAt = instant(obj.text("accessExpiresAt", 40)),
        refreshExpiresAt = instant(obj.text("refreshExpiresAt", 40))
    )

    private fun device(obj: JsonValue.Obj): AccountDevice {
        val createdAt = instant(obj.text("createdAt", 40))
        val lastSeenAt = instant(obj.text("lastSeenAt", 40))
        val expiresAt = instant(obj.text("expiresAt", 40))
        if (lastSeenAt < createdAt || !expiresAt.isAfter(createdAt)) throw JsonFail()
        return AccountDevice(
            AccountValidation.id(obj.text("familyId", 36)),
            AccountValidation.id(obj.text("deviceId", 36)),
            AccountValidation.deviceName(obj.text("deviceName", 80, true)),
            AccountValidation.platform(obj.text("platform", 16)),
            createdAt,
            lastSeenAt,
            expiresAt,
            obj.bool("isCurrent")
        )
    }

    private fun exportJob(obj: JsonValue.Obj) = AccountExportJob(
        AccountValidation.id(obj.text("exportId", 36)),
        obj.text("status", 32, true),
        instant(obj.text("createdAt", 40)),
        nullableInstant(obj, "completedAt"),
        nullableInstant(obj, "expiresAt")
    )

    private fun proof(obj: JsonValue.Obj) = AccountReauthProof(
        AccountValidation.opaque(obj.text("proofToken", 128), 43, 128),
        obj.text("purpose", 64, true),
        instant(obj.text("expiresAt", 40))
    )

    private fun proofBody(proofToken: String): String =
        "{\"proofToken\":${q(AccountValidation.opaque(proofToken, 1, 128))}}"

    private fun startBody(request: AccountExternalStartRequest): String {
        if (request.nativeChallengeMethod != "S256") throw AccountClientException(AccountClientFailure.InvalidRequest)
        val platform = AccountValidation.platform(request.platform)
        if (request.returnKind != platform) throw AccountClientException(AccountClientFailure.InvalidRequest)
        if (platform == "android" && request.returnPort != null) throw AccountClientException(AccountClientFailure.InvalidRequest)
        if (platform == "windows" && (request.returnPort == null || request.returnPort !in 1024..65535)) {
            throw AccountClientException(AccountClientFailure.InvalidRequest)
        }
        return buildString {
            append("{\"purpose\":").append(q(request.purpose))
            append(",\"nativeChallenge\":").append(q(AccountValidation.opaque(request.nativeChallenge, 43, 43)))
            append(",\"nativeChallengeMethod\":\"S256\"")
            append(",\"device\":{\"deviceId\":").append(q(AccountValidation.id(request.deviceId)))
            append(",\"deviceName\":").append(q(AccountValidation.deviceName(request.deviceName)))
            append(",\"platform\":").append(q(platform)).append('}')
            append(",\"nativeReturn\":{\"kind\":").append(q(request.returnKind))
            if (request.returnPort != null) append(",\"port\":").append(request.returnPort)
            append('}')
            if (request.proofToken != null) append(",\"proofToken\":").append(q(AccountValidation.opaque(request.proofToken, 1, 128)))
            if (request.proofPurpose != null) append(",\"proofPurpose\":").append(q(request.proofPurpose))
            append('}')
        }
    }

    private fun requireProvider(value: String): String =
        if (value == "vk" || value == "yandex") value else throw AccountClientException(AccountClientFailure.InvalidRequest)

    private fun optionalObj(obj: JsonValue.Obj, name: String): JsonValue.Obj? {
        val value = obj.fields[name] ?: return null
        return when (value) {
            is JsonValue.Null -> null
            is JsonValue.Obj -> value
            else -> throw JsonFail()
        }
    }

    private fun instant(raw: String): Instant = OffsetDateTime.parse(raw).toInstant()

    private fun nullableInstant(obj: JsonValue.Obj, name: String): Instant? =
        if (obj.field(name) is JsonValue.Null) null else instant(obj.text(name, 40))

    private fun fileName(headers: Map<String, String>, exportId: String): String {
        val header = headers.entries.firstOrNull { it.key.equals("Content-Disposition", true) }?.value
        val match = header?.let { FILENAME.find(it) }?.groupValues?.get(1)?.trim('"')
        if (match.isNullOrBlank()) return "zapara-export-$exportId.json"
        if (match.length > 180 || match.any { it == '/' || it == '\\' || it == '\u0000' }) {
            throw AccountClientException(AccountClientFailure.InvalidPayload)
        }
        return match
    }

    private suspend fun <T> readPayload(block: suspend () -> T): T = try {
        block()
    } catch (e: AccountClientException) {
        throw e
    } catch (_: Exception) {
        throw AccountClientException(AccountClientFailure.InvalidPayload)
    }

    private fun q(value: String): String = buildString {
        append('"')
        for (c in value) when (c) {
            '\\' -> append("\\\\")
            '"' -> append("\\\"")
            '\n' -> append("\\n")
            '\r' -> append("\\r")
            '\t' -> append("\\t")
            else -> append(c)
        }
        append('"')
    }

    companion object {
        private const val JSON_REQUEST_MAX = 16384
        private const val JSON_MAX = 65536
        private const val DOWNLOAD_MAX = 16 * 1024 * 1024
        private val FILENAME = Regex("filename\\*?=(?:UTF-8''|\"?)([^\\s\";]+)", RegexOption.IGNORE_CASE)

        fun deviceId(prefs: android.content.SharedPreferences): String {
            val existing = prefs.getString("deviceId", null)
            if (existing != null) return existing
            val id = UUID.randomUUID().toString()
            prefs.edit().putString("deviceId", id).commit()
            return id
        }
    }
}
