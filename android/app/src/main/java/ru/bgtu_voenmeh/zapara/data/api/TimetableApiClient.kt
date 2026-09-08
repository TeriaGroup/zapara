package ru.bgtu_voenmeh.zapara.data.api

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withTimeout
import java.net.InetAddress
import java.net.URI
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ConcurrentLinkedQueue

class TimetableApiClient(
    private val transport: HttpExchange,
    baseUri: URI
) {
    private val base: URI = validateBaseUri(baseUri)

    suspend fun fetch(requiredGroupIds: Iterable<String>): TimetableApiSnapshot {
        val ids = sortedSetOf<String>()
        for (id in requiredGroupIds) {
            TimetableApiJson.require(TimetableApiJson.validId(id))
            ids.add(id)
            TimetableApiJson.require(ids.size <= 5000)
        }
        try {
            return withTimeout(30_000) {
                var attempt = 0
                while (true) {
                    val catalog = TimetableApiJson.catalog(get("api/v1/groups", false))
                    val byId = catalog.groups.associateBy { it.id }
                    if (ids.any { it !in byId }) throw TimetableApiException(TimetableApiFailure.UnknownRequiredGroup)
                    try {
                        val downloaded = download(catalog, ids.map { byId.getValue(it) })
                        return@withTimeout catalog.copy(downloaded = downloaded)
                    } catch (e: TimetableApiException) {
                        if (e.failure == TimetableApiFailure.SnapshotUnavailable && attempt == 0) {
                            attempt++
                            continue
                        }
                        throw e
                    }
                }
                error("unreachable")
            }
        } catch (e: kotlinx.coroutines.TimeoutCancellationException) {
            throw TimetableApiException(TimetableApiFailure.Timeout)
        } catch (e: CancellationException) {
            throw e
        } catch (e: TimetableApiException) {
            throw e
        } catch (_: JsonFail) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
    }

    private suspend fun download(
        catalog: TimetableApiSnapshot,
        groups: List<TimetableApiGroup>
    ): Map<String, TimetableApiDownloadedGroup> = coroutineScope {
        if (groups.isEmpty()) return@coroutineScope emptyMap()
        val slots = Semaphore(4)
        val errors = ConcurrentLinkedQueue<TimetableApiException>()
        val rows = ConcurrentHashMap<String, TimetableApiDownloadedGroup>()
        val jobs = groups.map { group ->
            async {
                slots.withPermit {
                    try {
                        val escaped = escapeId(group.id)
                        val path = "api/v1/groups/$escaped/timetable?snapshotId=${catalog.meta.snapshotId}"
                        val json = get(path, true)
                        rows[group.id] = TimetableApiJson.timetable(json, catalog, group)
                    } catch (e: TimetableApiException) {
                        errors.add(e)
                        throw e
                    }
                }
            }
        }
        try {
            jobs.awaitAll()
        } catch (_: Exception) {
            val first = errors.firstOrNull() ?: throw TimetableApiException(TimetableApiFailure.Transport)
            throw errors.firstOrNull { it.failure != TimetableApiFailure.SnapshotUnavailable } ?: first
        }
        HashMap(rows)
    }

    private suspend fun get(path: String, pinned: Boolean): JsonValue {
        val url = base.toString() + path
        val reply = try {
            transport.exchange(HttpCall("GET", url, mapOf("Accept" to "application/json"), maxBytes = MAX_BYTES))
        } catch (e: CancellationException) {
            throw e
        } catch (_: HttpBodyTooLargeException) {
            throw TimetableApiException(TimetableApiFailure.BodyTooLarge)
        } catch (_: java.io.IOException) {
            throw TimetableApiException(TimetableApiFailure.Transport)
        }
        if (reply.headers.keys.any { it.equals("Content-Encoding", true) && reply.headers.getValue(it).isNotBlank() }) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
        if (reply.body.size > MAX_BYTES) throw TimetableApiException(TimetableApiFailure.BodyTooLarge)
        if (reply.status != 200) {
            if (pinned && reply.status == 404) {
                try {
                    val error = StrictJson.parse(reply.body, 32).obj()
                    if (error.int("status") == 404 && error.text("code", 64) == "snapshot_not_found") {
                        throw TimetableApiException(TimetableApiFailure.SnapshotUnavailable)
                    }
                } catch (e: TimetableApiException) {
                    throw e
                } catch (_: Exception) {
                }
            }
            throw TimetableApiException(TimetableApiFailure.ServerUnavailable)
        }
        return try {
            StrictJson.parse(reply.body, 32)
        } catch (_: JsonFail) {
            throw TimetableApiException(TimetableApiFailure.InvalidPayload)
        }
    }

    companion object {
        private const val MAX_BYTES = 16 * 1024 * 1024

        fun escapeId(id: String): String = when (id) {
            "." -> "%2E"
            ".." -> "%2E%2E"
            else -> URLEncoder.encode(id, StandardCharsets.UTF_8).replace("+", "%20")
        }

        fun validateBaseUri(uri: URI): URI {
            val loopback = uri.isAbsolute && isLoopback(uri)
            if (!uri.isAbsolute ||
                (uri.scheme != "https" && !(uri.scheme == "http" && loopback)) ||
                !uri.userInfo.isNullOrEmpty() ||
                !uri.query.isNullOrEmpty() ||
                !uri.fragment.isNullOrEmpty()
            ) {
                throw IllegalArgumentException("Некорректный адрес API.")
            }
            val raw = uri.toString().trimEnd('/') + "/"
            return URI(raw)
        }

        fun isLoopback(uri: URI): Boolean {
            val host = uri.host ?: return false
            if (host.equals("localhost", ignoreCase = true)) return true
            val stripped = host.trimStart('[').trimEnd(']')
            return try {
                if (stripped.contains(':')) {
                    InetAddress.getByName(stripped).isLoopbackAddress
                } else {
                    val parts = stripped.split('.')
                    if (parts.size != 4) return false
                    val bytes = ByteArray(4) { i ->
                        val n = parts[i].toIntOrNull() ?: return false
                        if (n !in 0..255) return false
                        n.toByte()
                    }
                    InetAddress.getByAddress(bytes).isLoopbackAddress
                }
            } catch (_: Exception) {
                false
            }
        }
    }
}
