package ru.bgtu_voenmeh.zapara.data.sync

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.withTimeout
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation
import ru.bgtu_voenmeh.zapara.data.api.HttpBodyTooLargeException
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.io.IOException
import java.net.URI
import java.time.Clock
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

class PrivateSyncHttpClient(
    private val transport: HttpExchange,
    baseUri: URI,
    private val timeoutMs: Long = 30_000,
    private val clock: Clock = Clock.systemUTC()
) {
    val scope: AccountServerScope = AccountServerScope.parse(baseUri.toString())

    suspend fun metadata(access: String): PrivateSyncResult<SyncMetadata> =
        send("GET", "metadata", access, null) { SyncJson.metadata(it) }

    suspend fun mutate(access: String, request: SyncMutation): PrivateSyncResult<SyncMutationResult> {
        val body = try {
            SyncJson.serialize(request)
        } catch (_: IllegalArgumentException) {
            return PrivateSyncResult(PrivateSyncState.InvalidRequest)
        }
        if (body.size > SyncValidation.REQUEST_BYTES) return PrivateSyncResult(PrivateSyncState.InvalidRequest)
        return send("POST", "mutations", access, body, mutation = true) { bytes ->
            val result = SyncJson.mutationResult(bytes)
            if (!matches(result, request)) SyncValidation.invalid()
            result
        }
    }

    suspend fun changes(
        access: String,
        epoch: UUID,
        afterSequence: Long,
        limit: Int = 100
    ): PrivateSyncResult<SyncChangesPage> {
        if (epoch == UUID(0, 0) || !validPage(afterSequence, limit)) {
            return PrivateSyncResult(PrivateSyncState.InvalidRequest)
        }
        val path = "changes?epoch=$epoch&afterSequence=$afterSequence&limit=$limit"
        return send("GET", path, access, null, page = true, cursorReset = true) { bytes ->
            val page = SyncJson.changesPage(bytes)
            if (page.metadata.syncEpoch != epoch || page.afterSequence != afterSequence || page.changes.size > limit) {
                SyncValidation.invalid()
            }
            page
        }
    }

    suspend fun beginResync(access: String): PrivateSyncResult<SyncResyncManifest> =
        send("POST", "resync", access, null) { SyncJson.resyncManifest(it) }

    suspend fun readResyncPage(
        access: String,
        manifest: SyncResyncManifest,
        afterOrdinal: Long,
        limit: Int = 100
    ): PrivateSyncResult<SyncResyncPage> {
        if (!validPage(afterOrdinal, limit) || afterOrdinal > manifest.itemCount) {
            return PrivateSyncResult(PrivateSyncState.InvalidRequest)
        }
        val path = "resync/${manifest.manifestId}?afterOrdinal=$afterOrdinal&limit=$limit"
        return send("GET", path, access, null, page = true, manifestExpired = true) { bytes ->
            val page = SyncJson.resyncPage(bytes)
            if (page.manifest != manifest || page.afterOrdinal != afterOrdinal || page.items.size > limit) {
                SyncValidation.invalid()
            }
            page
        }
    }

    private suspend fun <T> send(
        method: String,
        path: String,
        access: String,
        body: ByteArray?,
        page: Boolean = false,
        mutation: Boolean = false,
        cursorReset: Boolean = false,
        manifestExpired: Boolean = false,
        parse: (ByteArray) -> T
    ): PrivateSyncResult<T> {
        return try {
            withTimeout(timeoutMs) {
                sendOnce(method, path, access, body, page, parse, mutation, cursorReset, manifestExpired)
            }
        } catch (_: TimeoutCancellationException) {
            PrivateSyncResult(PrivateSyncState.TimedOut)
        } catch (_: CancellationException) {
            PrivateSyncResult(PrivateSyncState.Cancelled)
        }
    }

    private suspend fun <T> sendOnce(
        method: String,
        path: String,
        access: String,
        body: ByteArray?,
        page: Boolean,
        parse: (ByteArray) -> T,
        mutation: Boolean,
        cursorReset: Boolean,
        manifestExpired: Boolean
    ): PrivateSyncResult<T> {
        var sending = false
        try {
            AccountValidation.token(access, "za_")
            val headers = linkedMapOf("Accept" to "application/json", "Authorization" to "Bearer $access")
            if (body != null) headers["Content-Type"] = "application/json; charset=utf-8"
            val url = scope.baseUri.toString() + "api/v1/sync/" + path
            sending = true
            val probeLimit = if (page) SyncValidation.PAGE_BYTES else SyncValidation.REQUEST_BYTES
            val reply = try {
                transport.exchange(HttpCall(method, url, headers, body, maxBytes = probeLimit))
            } catch (e: CancellationException) {
                throw e
            } catch (_: HttpBodyTooLargeException) {
                return PrivateSyncResult(PrivateSyncState.InvalidResponse)
            } catch (_: IOException) {
                return PrivateSyncResult(PrivateSyncState.Unavailable)
            }
            val status = reply.status
            if (status in 300..399) return PrivateSyncResult(PrivateSyncState.InvalidResponse)
            if (hasContentEncoding(reply)) return PrivateSyncResult(PrivateSyncState.InvalidResponse)
            val success = status == 200 || (mutation && (status == 409 || status == 410))
            val limit = when {
                !success -> 4096
                page -> SyncValidation.PAGE_BYTES
                else -> SyncValidation.REQUEST_BYTES
            }
            if (reply.body.size > limit) return PrivateSyncResult(PrivateSyncState.InvalidResponse)
            if (!jsonMedia(reply.contentType, !success)) return PrivateSyncResult(PrivateSyncState.InvalidResponse)
            return decode(reply, status, mutation, cursorReset, manifestExpired, parse)
        } catch (e: CancellationException) {
            throw e
        } catch (_: IllegalArgumentException) {
            return PrivateSyncResult(if (sending) PrivateSyncState.InvalidResponse else PrivateSyncState.InvalidRequest)
        } catch (_: IllegalStateException) {
            return PrivateSyncResult(if (sending) PrivateSyncState.InvalidResponse else PrivateSyncState.InvalidRequest)
        }
    }

    private fun <T> decode(
        reply: HttpReply,
        status: Int,
        mutation: Boolean,
        cursorReset: Boolean,
        manifestExpired: Boolean,
        parse: (ByteArray) -> T
    ): PrivateSyncResult<T> {
        if (status == 200 || (mutation && (status == 409 || status == 410))) {
            val value = try {
                parse(reply.body)
            } catch (_: Exception) {
                return PrivateSyncResult(PrivateSyncState.InvalidResponse)
            }
            if (value is SyncMutationResult) {
                if (value.status != status) return PrivateSyncResult(PrivateSyncState.InvalidResponse)
                @Suppress("UNCHECKED_CAST")
                return PrivateSyncResult(
                    when (status) {
                        200 -> PrivateSyncState.Success
                        409 -> PrivateSyncState.Conflict
                        else -> PrivateSyncState.ResetRequired
                    },
                    if (status == 200) value as T else null,
                    value
                )
            }
            return PrivateSyncResult(PrivateSyncState.Success, value)
        }
        val error = try {
            SyncJson.error(reply.body)
        } catch (_: Exception) {
            return PrivateSyncResult(PrivateSyncState.InvalidResponse)
        }
        if (error.status != status) return PrivateSyncResult(PrivateSyncState.InvalidResponse)
        val state = when {
            status == 400 || status == 413 -> PrivateSyncState.InvalidRequest
            status == 401 -> PrivateSyncState.NeedsReauthentication
            status == 410 && error.code == "sync_reset" && cursorReset -> PrivateSyncState.ResetRequired
            status == 410 && error.code == "manifest_expired" && manifestExpired -> PrivateSyncState.ManifestExpired
            status == 429 -> PrivateSyncState.RateLimited
            status == 503 -> PrivateSyncState.Unavailable
            else -> PrivateSyncState.InvalidResponse
        }
        val retry = if (state == PrivateSyncState.RateLimited) retryAfter(reply.headers) else null
        return PrivateSyncResult(state, retryAfter = retry)
    }

    private fun matches(result: SyncMutationResult, request: SyncMutation): Boolean {
        if (result.status == 410) return true
        if (result.metadata.syncEpoch != request.syncEpoch) return false
        val record = result.serverRecord ?: return result.status != 200
        if (record.entityId != request.entityId || record.entityType != request.entityType ||
            record.revision > result.metadata.currentSequence
        ) return false
        return result.status != 200 || (
            record.revision > request.expectedRevision &&
                record.tombstone == (request.action == "delete") &&
                record.value == request.value
            )
    }

    private fun jsonMedia(contentType: String?, allowProblem: Boolean): Boolean {
        if (contentType == null) return false
        val parts = contentType.split(';').map { it.trim() }
        if (parts.isEmpty()) return false
        val media = parts[0].lowercase(Locale.ROOT)
        val ok = media == "application/json" || (allowProblem && media == "application/problem+json")
        if (!ok) return false
        for (part in parts.drop(1)) {
            val eq = part.indexOf('=')
            if (eq <= 0) continue
            val key = part.substring(0, eq).trim().lowercase(Locale.ROOT)
            val value = part.substring(eq + 1).trim().trim('"')
            if (key == "charset" && !value.equals("utf-8", ignoreCase = true)) return false
        }
        return true
    }

    private fun hasContentEncoding(reply: HttpReply): Boolean {
        val value = reply.headers.entries.firstOrNull { it.key.equals("Content-Encoding", true) }?.value
        return !value.isNullOrBlank()
    }

    private fun retryAfter(headers: Map<String, String>): Duration? {
        val raw = headers.entries.firstOrNull { it.key.equals("Retry-After", true) }?.value ?: return null
        val seconds = raw.toLongOrNull()
        val delay = if (seconds != null) Duration.ofSeconds(seconds) else {
            val date = parseHttpDate(raw) ?: return null
            Duration.between(clock.instant(), date)
        }
        val clamped = delay.seconds.coerceIn(0, 300)
        return Duration.ofSeconds(clamped)
    }

    private fun parseHttpDate(raw: String): Instant? = try {
        DateTimeFormatter.RFC_1123_DATE_TIME.withZone(ZoneOffset.UTC).parse(raw, Instant::from)
    } catch (_: Exception) {
        null
    }

    companion object {
        fun pullNextAfterSequence(page: SyncChangesPage): Long = page.nextAfterSequence
        private fun validPage(after: Long, limit: Int): Boolean =
            after >= 0 && limit in 1..SyncValidation.PAGE_RECORDS
    }
}
