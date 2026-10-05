package ru.bgtu_voenmeh.zapara.data.sync

import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.net.URI
import java.time.Instant
import java.time.LocalDate
import java.util.Base64
import java.util.UUID

internal val ACCESS: String = run {
    val encoded = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { 1 })
    "za_$encoded"
}

internal val EPOCH: UUID = UUID.fromString("11111111-1111-1111-1111-111111111111")
internal val OP: UUID = UUID.fromString("22222222-2222-2222-2222-222222222222")
internal val ENTITY: UUID = UUID.fromString("33333333-3333-3333-3333-333333333333")
internal val MANIFEST_ID: UUID = UUID.fromString("44444444-4444-4444-4444-444444444444")
internal val NOW: Instant = Instant.parse("2026-09-08T00:00:00Z")
internal val CREATED: Instant = Instant.parse("2026-09-05T12:00:00Z")
internal const val ROOT = "https://example.test/root/"

internal class FakeHttp(var handler: suspend (HttpCall) -> HttpReply) : HttpExchange {
    val requests = mutableListOf<HttpCall>()
    override suspend fun exchange(call: HttpCall): HttpReply {
        synchronized(requests) { requests += call }
        return handler(call)
    }
}

internal fun jsonReply(status: Int, body: String, contentType: String? = "application/json", headers: Map<String, String> = emptyMap()) =
    HttpReply(status, body.toByteArray(Charsets.UTF_8), contentType, headers)

internal fun rawReply(
    status: Int,
    body: ByteArray,
    contentType: String? = "application/json",
    headers: Map<String, String> = emptyMap()
) = HttpReply(status, body, contentType, headers)

internal fun client(http: HttpExchange, timeoutMs: Long = 30_000) =
    PrivateSyncHttpClient(http, URI(ROOT), timeoutMs)

internal fun completionValue(done: Boolean = true, at: Instant? = NOW) = CompletionValue(done, at)

internal fun homeworkValue(
    text: String = "глава 1",
    raw: String = "лек ИСТОРИЯ",
    nth: Int = 1,
    created: Instant = CREATED,
    legacy: LocalDate? = null
) = HomeworkValue(raw, SyncValidation.normalizeSubject(raw), text, nth, created, legacy)

internal fun completionRecord(
    entityId: UUID = ENTITY,
    revision: Long = 1,
    tombstone: Boolean = false,
    value: CompletionValue? = completionValue()
) = SyncRecord("completion", entityId, revision, tombstone, NOW, value)

internal fun homeworkRecord(
    entityId: UUID = ENTITY,
    revision: Long = 1,
    value: HomeworkValue = homeworkValue()
) = SyncRecord("homework", entityId, revision, false, NOW, value)

internal fun metadataJson(epoch: UUID = EPOCH, current: Long = 9, min: Long = 0) =
    """{"syncEpoch":"$epoch","currentSequence":$current,"minAfterSequence":$min}"""

internal fun completionValueJson(done: Boolean = true, at: Instant? = NOW): String {
    val doneAt = if (at == null) "null" else "\"$at\"".replace(".000Z", "Z")
    return """{"done":$done,"doneAtUtc":$doneAt}"""
}

internal fun recordJson(
    type: String = "completion",
    entityId: UUID = ENTITY,
    revision: Long = 1,
    tombstone: Boolean = false,
    value: String? = completionValueJson()
): String {
    val v = value ?: "null"
    return """{"entityType":"$type","entityId":"$entityId","revision":$revision,"tombstone":$tombstone,"changedAt":"2026-09-08T00:00:00Z","value":$v}"""
}

internal fun mutationResultJson(
    status: Int,
    code: String,
    metadata: String = metadataJson(),
    record: String? = null
) = """{"status":$status,"code":"$code","metadata":$metadata,"serverRecord":${record ?: "null"}}"""

internal fun changesPageJson(
    metadata: String = metadataJson(),
    after: Long = 0,
    next: Long = 1,
    hasMore: Boolean = true,
    changes: String = """[{"sequence":1,"opId":"$OP","record":${recordJson()}}]"""
) = """{"metadata":$metadata,"afterSequence":$after,"nextAfterSequence":$next,"hasMore":$hasMore,"changes":$changes}"""

internal fun manifestJson(
    id: UUID = MANIFEST_ID,
    epoch: UUID = EPOCH,
    highWater: Long = 9,
    created: String = "2026-09-08T00:00:00Z",
    expires: String = "2026-09-08T00:10:00Z",
    itemCount: Long = 1
) = """{"manifestId":"$id","syncEpoch":"$epoch","highWater":$highWater,"createdAt":"$created","expiresAt":"$expires","itemCount":$itemCount}"""

internal fun resyncPageJson(
    manifest: String = manifestJson(),
    after: Long = 0,
    next: Long = 1,
    hasMore: Boolean = false,
    items: String = """[{"ordinal":1,"record":${recordJson()}}]"""
) = """{"manifest":$manifest,"afterOrdinal":$after,"nextAfterOrdinal":$next,"hasMore":$hasMore,"items":$items}"""

internal fun errorJson(status: Int, code: String, title: String? = null): String {
    val extra = if (title == null) "" else """"title":"$title","""
    return """{$extra"status":$status,"code":"$code"}"""
}

internal fun mutation(
    epoch: UUID = EPOCH,
    opId: UUID = OP,
    type: String = "completion",
    entityId: UUID = ENTITY,
    revision: Long = 0,
    action: String = "upsert",
    value: SyncValue? = completionValue()
) = SyncMutation(epoch, opId, type, entityId, revision, action, value)

internal fun manifest() = SyncResyncManifest(
    MANIFEST_ID, EPOCH, 9, NOW, NOW.plusSeconds(600), 1
)
