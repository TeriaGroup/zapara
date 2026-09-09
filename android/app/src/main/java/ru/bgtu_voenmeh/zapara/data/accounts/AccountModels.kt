package ru.bgtu_voenmeh.zapara.data.accounts

import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.bool
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.int
import ru.bgtu_voenmeh.zapara.data.api.nullableText
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text
import java.time.Instant
import java.util.Locale

enum class AccountClientFailure {
    InvalidRequest, InvalidCredentials, InvalidSession, UsernameUnavailable, SessionNotFound,
    RateLimited, DbUnavailable, RegistrationUnavailable, NotConfigured, ServerUnavailable,
    InvalidPayload, BodyTooLarge, Transport, Timeout, VaultUnavailable, ReauthenticationRequired,
    SessionChanged, LockTimeout, InternalError, InvalidExternalProof, ProviderUnavailable,
    ExportNotFound, RecoveryUnavailable, ExternalAttemptExpired, LastLoginMethod,
    IdentityUnavailable, PasswordAlreadySet
}

class AccountClientException(val failure: AccountClientFailure) : Exception("Операция аккаунта не выполнена.")

data class AccountUser(
    val userId: String,
    val username: String,
    val displayName: String?,
    val createdAt: Instant
)

data class AccountSession(
    val user: AccountUser,
    val familyId: String,
    val accessToken: String,
    val refreshToken: String,
    val tokenType: String,
    val accessExpiresAt: Instant,
    val refreshExpiresAt: Instant
) {
    override fun toString(): String = "AccountSession { [REDACTED] }"
}

data class AccountDevice(
    val familyId: String,
    val deviceId: String,
    val deviceName: String,
    val platform: String,
    val createdAt: Instant,
    val lastSeenAt: Instant,
    val expiresAt: Instant,
    val isCurrent: Boolean
)

data class AccountDevicesPage(
    val devices: List<AccountDevice>,
    val nextCursor: String?
)

data class AccountExportJob(
    val exportId: String,
    val status: String,
    val createdAt: Instant,
    val completedAt: Instant?,
    val expiresAt: Instant?
)

data class AccountExportDownload(
    val bytes: ByteArray,
    val fileName: String
) {
    override fun toString(): String = "AccountExportDownload { ${bytes.size} }"
}

data class AccountDeleteResponse(
    val status: String,
    val remoteWipe: Boolean
)

data class AccountReauthProof(
    val proofToken: String,
    val purpose: String,
    val expiresAt: Instant
) {
    override fun toString(): String = "AccountReauthProof { [REDACTED] }"
}

data class AccountExternalStartRequest(
    val purpose: String,
    val nativeChallenge: String,
    val nativeChallengeMethod: String,
    val deviceId: String,
    val deviceName: String,
    val platform: String,
    val returnKind: String,
    val returnPort: Int? = null,
    val proofToken: String? = null,
    val proofPurpose: String? = null
) {
    override fun toString(): String = "AccountExternalStartRequest { [REDACTED] }"
}

data class AccountExternalStart(
    val transactionId: String,
    val authorizeUrl: String,
    val expiresAt: Instant
) {
    override fun toString(): String = "AccountExternalStart { [REDACTED] }"
}

data class AccountExternalExchangeRequest(
    val transactionId: String,
    val nativeVerifier: String,
    val handoffCode: String
) {
    override fun toString(): String = "AccountExternalExchangeRequest { [REDACTED] }"
}

data class AccountExternalExchange(
    val status: String,
    val session: AccountSession?,
    val proof: AccountReauthProof?
) {
    override fun toString(): String = "AccountExternalExchange { [REDACTED] }"
}

data class AccountExternalStatus(val status: String)

data class AccountExternalIdentity(
    val provider: String,
    val linkedAt: Instant
)

enum class AccountRefreshState { Ready, Pending }

data class AccountVaultEntry(
    val version: Int,
    val serverKey: String,
    val userId: String,
    val familyId: String,
    val session: AccountSession,
    val refreshState: AccountRefreshState
) {
    override fun toString(): String = "AccountVaultEntry { [REDACTED] }"

    fun encode(): String = buildString {
        append('{')
        append("\"version\":").append(version).append(',')
        append("\"serverKey\":").append(jsonString(serverKey)).append(',')
        append("\"userId\":").append(jsonString(userId)).append(',')
        append("\"familyId\":").append(jsonString(familyId)).append(',')
        append("\"refreshState\":").append(jsonString(refreshState.name)).append(',')
        append("\"session\":{")
        append("\"user\":{")
        append("\"userId\":").append(jsonString(session.user.userId)).append(',')
        append("\"username\":").append(jsonString(session.user.username)).append(',')
        append("\"displayName\":")
        if (session.user.displayName == null) append("null") else append(jsonString(session.user.displayName))
        append(",\"createdAt\":").append(jsonString(session.user.createdAt.toString()))
        append("},\"familyId\":").append(jsonString(session.familyId))
        append(",\"accessToken\":").append(jsonString(session.accessToken))
        append(",\"refreshToken\":").append(jsonString(session.refreshToken))
        append(",\"tokenType\":").append(jsonString(session.tokenType))
        append(",\"accessExpiresAt\":").append(jsonString(session.accessExpiresAt.toString()))
        append(",\"refreshExpiresAt\":").append(jsonString(session.refreshExpiresAt.toString()))
        append("}}")
    }

    companion object {
        fun ready(serverKey: String, session: AccountSession) = AccountVaultEntry(
            1, serverKey, session.user.userId, session.familyId, session, AccountRefreshState.Ready
        )

        fun decode(json: String, expectedKey: String): AccountVaultEntry {
            val root = try {
                StrictJson.parse(json).obj()
            } catch (_: JsonFail) {
                throw AccountClientException(AccountClientFailure.VaultUnavailable)
            }
            try {
                val version = root.int("version")
                val serverKey = root.text("serverKey", 64)
                if (version != 1 || serverKey != expectedKey) throw AccountClientException(AccountClientFailure.VaultUnavailable)
                val sessionObj = root.field("session").obj()
                val userObj = sessionObj.field("user").obj()
                val session = AccountSession(
                    user = AccountUser(
                        AccountValidation.id(userObj.text("userId", 36)),
                        AccountValidation.username(userObj.text("username", 32)),
                        userObj.nullableText("displayName", 80)?.let { AccountValidation.displayName(it) },
                        Instant.parse(userObj.text("createdAt", 40))
                    ),
                    familyId = AccountValidation.id(sessionObj.text("familyId", 36)),
                    accessToken = AccountValidation.token(sessionObj.text("accessToken", 46), "za_"),
                    refreshToken = AccountValidation.token(sessionObj.text("refreshToken", 46), "zr_"),
                    tokenType = sessionObj.text("tokenType", 16).also { if (it != "Bearer") throw JsonFail() },
                    accessExpiresAt = Instant.parse(sessionObj.text("accessExpiresAt", 40)),
                    refreshExpiresAt = Instant.parse(sessionObj.text("refreshExpiresAt", 40))
                )
                val state = AccountRefreshState.valueOf(root.text("refreshState", 16))
                val userId = AccountValidation.id(root.text("userId", 36))
                val familyId = AccountValidation.id(root.text("familyId", 36))
                if (userId != session.user.userId || familyId != session.familyId) {
                    throw AccountClientException(AccountClientFailure.VaultUnavailable)
                }
                return AccountVaultEntry(version, serverKey, userId, familyId, session, state)
            } catch (e: AccountClientException) {
                throw e
            } catch (_: Exception) {
                throw AccountClientException(AccountClientFailure.VaultUnavailable)
            }
        }
    }
}

private fun jsonString(value: String): String = buildString {
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

object AccountValidation {
    private val usernameRe = Regex("\\A[A-Za-z0-9_.-]{3,32}\\z")
    private val tokenBody = Regex("\\A[A-Za-z0-9_-]{43}\\z")
    private val uuidRe = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

    fun username(value: String?): String {
        if (value == null || !usernameRe.matches(value)) throw invalid()
        return value
    }

    fun normalizeUsername(value: String): String = username(value).lowercase(Locale.ROOT)

    fun password(value: String?): String {
        scalars(value, 12, 128, false)
        return value!!
    }

    fun displayName(value: String?): String? {
        if (value != null) scalars(value, 1, 80, true)
        return value
    }

    fun deviceName(value: String?): String {
        scalars(value, 1, 80, true)
        return value!!
    }

    fun id(value: String): String {
        if (!uuidRe.matches(value) || value == "00000000-0000-0000-0000-000000000000") throw invalid()
        return value.lowercase(Locale.ROOT)
    }

    fun platform(value: String?): String =
        if (value == "windows" || value == "android") value else throw invalid()

    fun opaque(value: String?, min: Int = 43, max: Int = 128): String {
        if (value == null || value.length < min || value.length > max) throw invalid()
        for (c in value) {
            if (c !in 'A'..'Z' && c !in 'a'..'z' && c !in '0'..'9' && c != '-' && c != '_') throw invalid()
        }
        return value
    }

    fun asciiCursor(value: String): Boolean =
        value.length == 55 && value.all { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' || it == '_' || it == '-' }

    fun token(value: String?, prefix: String): String {
        if (value == null || value.length != 46 || !value.startsWith(prefix)) throw invalid()
        val encoded = value.substring(3)
        if (!tokenBody.matches(encoded)) throw invalid()
        val padded = encoded.replace('-', '+').replace('_', '/') + "="
        val bytes = try {
            java.util.Base64.getDecoder().decode(padded)
        } catch (_: Exception) {
            throw invalid()
        }
        val canonical = java.util.Base64.getEncoder().encodeToString(bytes)
            .trimEnd('=').replace('+', '-').replace('/', '_')
        if (canonical != encoded) throw invalid()
        return value
    }

    private fun scalars(value: String?, min: Int, max: Int, rejectControls: Boolean) {
        if (value == null || value.length > max * 2) throw invalid()
        var count = 0
        var i = 0
        while (i < value.length) {
            val cp = value.codePointAt(i)
            if (cp == 0 || (rejectControls && Character.isISOControl(cp))) throw invalid()
            count++
            i += Character.charCount(cp)
        }
        if (count < min || count > max) throw invalid()
    }

    private fun invalid(): IllegalArgumentException = IllegalArgumentException("Недопустимые данные аккаунта.")
}
