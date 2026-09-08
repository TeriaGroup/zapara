package ru.bgtu_voenmeh.zapara.data.accounts

import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.array
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.int
import ru.bgtu_voenmeh.zapara.data.api.nullableText
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text
import java.time.Instant
import java.util.UUID

class AccountHttpClient(
    private val transport: HttpExchange,
    val scope: AccountServerScope
) {
    data class Capabilities(val registration: Boolean)

    suspend fun capabilities(): Capabilities {
        val root = send("GET", "auth/capabilities", null, null, 200).obj()
        return Capabilities(root.field("registration").let { (it as? ru.bgtu_voenmeh.zapara.data.api.JsonValue.Bool)?.value ?: false })
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

    private suspend fun send(method: String, path: String, body: String?, access: String?, expected: Int): ru.bgtu_voenmeh.zapara.data.api.JsonValue {
        val headers = linkedMapOf("Accept" to "application/json")
        if (access != null) headers["Authorization"] = "Bearer $access"
        val bytes = body?.toByteArray(Charsets.UTF_8)
        if (bytes != null && bytes.size > 16384) throw AccountClientException(AccountClientFailure.InvalidRequest)
        val reply = try {
            transport.exchange(
                HttpCall(method, scope.baseUri.toString() + "api/v1/" + path, headers, bytes, maxBytes = 65536)
            )
        } catch (_: ru.bgtu_voenmeh.zapara.data.api.HttpBodyTooLargeException) {
            throw AccountClientException(AccountClientFailure.BodyTooLarge)
        } catch (_: java.io.IOException) {
            throw AccountClientException(AccountClientFailure.Transport)
        }
        if (reply.body.size > 65536) throw AccountClientException(AccountClientFailure.BodyTooLarge)
        if (reply.status != expected) throw mapError(reply.status, reply.body)
        if (expected == 204) return ru.bgtu_voenmeh.zapara.data.api.JsonValue.Null
        return try {
            StrictJson.parse(reply.body)
        } catch (_: JsonFail) {
            throw AccountClientException(AccountClientFailure.InvalidPayload)
        }
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
            409 to "username_unavailable" -> AccountClientFailure.UsernameUnavailable
            404 to "session_not_found" -> AccountClientFailure.SessionNotFound
            429 to "rate_limited" -> AccountClientFailure.RateLimited
            503 to "db_unavailable" -> AccountClientFailure.DbUnavailable
            503 to "registration_unavailable" -> AccountClientFailure.RegistrationUnavailable
            500 to "internal_error" -> AccountClientFailure.InternalError
            else -> if (status == 404) AccountClientFailure.NotConfigured else AccountClientFailure.ServerUnavailable
        }
        return AccountClientException(failure)
    }

    private fun user(obj: ru.bgtu_voenmeh.zapara.data.api.JsonValue.Obj) = AccountUser(
        AccountValidation.id(obj.text("userId", 36)),
        AccountValidation.username(obj.text("username", 32)),
        obj.nullableText("displayName", 80),
        Instant.parse(obj.text("createdAt", 40))
    )

    private fun session(obj: ru.bgtu_voenmeh.zapara.data.api.JsonValue.Obj) = AccountSession(
        user = user(obj.field("user").obj()),
        familyId = AccountValidation.id(obj.text("familyId", 36)),
        accessToken = AccountValidation.token(obj.text("accessToken", 46), "za_"),
        refreshToken = AccountValidation.token(obj.text("refreshToken", 46), "zr_"),
        tokenType = obj.text("tokenType", 16).also { if (it != "Bearer") throw AccountClientException(AccountClientFailure.InvalidPayload) },
        accessExpiresAt = Instant.parse(obj.text("accessExpiresAt", 40)),
        refreshExpiresAt = Instant.parse(obj.text("refreshExpiresAt", 40))
    )

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
        fun deviceId(prefs: android.content.SharedPreferences): String {
            val existing = prefs.getString("deviceId", null)
            if (existing != null) return existing
            val id = UUID.randomUUID().toString()
            prefs.edit().putString("deviceId", id).commit()
            return id
        }
    }
}
