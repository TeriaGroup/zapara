package ru.bgtu_voenmeh.zapara.data.social

import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation
import ru.bgtu_voenmeh.zapara.data.api.*
import ru.bgtu_voenmeh.zapara.data.communities.CommunityValidation
import java.time.Instant

enum class InboxSource { Group, GroupDirect, Friend }
data class InboxRow(
    val id: String,
    val title: String,
    val communityId: String? = null,
    val lastBody: String? = null,
    val lastAt: Instant? = null,
    val unread: Int = 0,
    val subtitle: String = if (communityId == null) "Личный чат" else "Учебная группа",
    val source: InboxSource = if (communityId == null) InboxSource.Friend else InboxSource.Group
)
fun orderInbox(rows: List<InboxRow>): List<InboxRow> = rows.distinctBy { it.id }.sortedWith(compareByDescending<InboxRow> { it.lastAt }.thenByDescending { it.unread > 0 }.thenBy { it.title })
fun groupInboxRows(home: ru.bgtu_voenmeh.zapara.data.communities.GroupHome): List<InboxRow> =
    (listOf(home.groupChat) + home.directs).map { conversation ->
        InboxRow(conversation.conversationId, conversation.title, home.communityId, conversation.lastBody, conversation.lastAt, conversation.unread,
            if (conversation.kind == "direct") "Личный чат · ${home.groupName ?: home.name}" else "Учебная группа",
            if (conversation.kind == "direct") InboxSource.GroupDirect else InboxSource.Group)
    }
data class SocialInvite(val id: String, val name: String)
data class SocialHome(val code: String, val friends: List<InboxRow>, val incoming: List<SocialInvite>, val outgoing: List<SocialInvite>)
data class SocialReaction(val emoji: String, val count: Int, val mine: Boolean)
data class SocialMessage(val id: String, val senderId: String, val senderName: String, val body: String?, val kind: String, val createdAt: Instant, val replyTo: String?, val replyBody: String?, val deleted: Boolean, val edited: Boolean, val read: Boolean, val reactions: List<SocialReaction>, val attachmentId: String?, val fileName: String?, val durationMs: Int? = null)
data class SocialPage(val messages: List<SocialMessage>, val hasMore: Boolean)
suspend fun loadSocialUpdates(knownIds: Set<String>, load: suspend (String?) -> SocialPage): SocialPage {
    var page = load(null)
    val hasMore = page.hasMore
    val cursors = mutableSetOf<String>()
    var collected = page.messages
    while (knownIds.isNotEmpty() && page.hasMore && page.messages.none { it.id in knownIds }) {
        val before = page.messages.firstOrNull()?.id ?: error("Пустая страница")
        check(cursors.add(before)) { "Повтор страницы" }
        page = load(before)
        collected = page.messages + collected
    }
    return SocialPage(collected.distinctBy { it.id }, hasMore)
}
class SocialFailure(val status: Int) : Exception("Не удалось выполнить запрос")

class SocialHttpClient(private val transport: HttpExchange, private val scope: AccountServerScope) {
    suspend fun home(token: String): SocialHome = parseHome(request(token, "GET", "/home").obj())
    suspend fun invite(token: String, code: String): SocialHome {
        val clean = code.trim()
        require(clean.isNotEmpty() && clean.length <= 64 && clean.all { it.isLetterOrDigit() || it == '-' })
        return parseHome(request(token, "POST", "/invites", "{\"code\":${quote(clean)}}").obj())
    }
    suspend fun respond(token: String, id: String, accept: Boolean): SocialHome = parseHome(request(token, "POST", "/invites/${id(id)}/${if (accept) "accept" else "decline"}").obj())
    suspend fun messages(token: String, conversation: String, before: String? = null): SocialPage {
        val obj = request(token, "GET", "/conversations/${id(conversation)}/messages" + (before?.let { "?before=${id(it)}" } ?: "")).obj()
        return SocialPage(obj.array("messages", 100).items.map { message(it.obj()) }, obj.bool("hasMore"))
    }
    suspend fun send(token: String, conversation: String, body: String, reply: String?): SocialMessage = message(request(token, "POST", "/conversations/${id(conversation)}/messages", textBody(body, reply), 201).obj())
    suspend fun edit(token: String, conversation: String, target: String, body: String): SocialMessage = message(request(token, "POST", "/conversations/${id(conversation)}/messages/${id(target)}/edit", textBody(body, null)).obj())
    suspend fun delete(token: String, conversation: String, target: String): SocialMessage = message(request(token, "POST", "/conversations/${id(conversation)}/messages/${id(target)}/delete").obj())
    suspend fun react(token: String, conversation: String, target: String, emoji: String): SocialMessage {
        require(emoji in setOf("like", "heart", "laugh", "wow", "sad"))
        return message(request(token, "POST", "/conversations/${id(conversation)}/messages/${id(target)}/reaction", "{\"emoji\":${quote(emoji)}}").obj())
    }
    suspend fun upload(token: String, conversation: String, name: String, bytes: ByteArray, image: Boolean, reply: String?): SocialMessage {
        return uploadMultipart(token, conversation, if (image) "images" else "files", name, bytes, "application/octet-stream", 20 * 1024 * 1024, null, reply)
    }
    suspend fun uploadRecording(token: String, conversation: String, kind: String, bytes: ByteArray, durationMs: Int, reply: String?): SocialMessage {
        require(kind == "voice" || kind == "circle")
        val voice = kind == "voice"
        require(durationMs in 1..(if (voice) 180_000 else 60_000))
        return uploadMultipart(token, conversation, if (voice) "voice" else "circles", if (voice) "voice.m4a" else "circle.mp4", bytes,
            if (voice) "audio/mp4" else "video/mp4", if (voice) 2 * 1024 * 1024 else 8 * 1024 * 1024, durationMs, reply)
    }
    private suspend fun uploadMultipart(token: String, conversation: String, route: String, name: String, bytes: ByteArray, mime: String, maxBytes: Int, durationMs: Int?, reply: String?): SocialMessage {
        require(bytes.isNotEmpty() && bytes.size <= maxBytes)
        val boundary = "zapara-${java.util.UUID.randomUUID()}"
        val safeName = name.replace(Regex("[\\r\\n\"\\\\]"), "_").take(180).ifBlank { "document" }
        val body = java.io.ByteArrayOutputStream()
        fun part(text: String) { body.write(text.toByteArray(Charsets.UTF_8)) }
        if (reply != null) part("--$boundary\r\nContent-Disposition: form-data; name=\"replyTo\"\r\n\r\n${id(reply)}\r\n")
        if (durationMs != null) part("--$boundary\r\nContent-Disposition: form-data; name=\"durationMs\"\r\n\r\n$durationMs\r\n")
        part("--$boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"$safeName\"\r\nContent-Type: $mime\r\n\r\n")
        body.write(bytes); part("\r\n--$boundary--\r\n")
        val response = transport.exchange(HttpCall("POST", scope.baseUri.toString() + "api/v1/social/conversations/${id(conversation)}/$route", mapOf("Authorization" to "Bearer ${AccountValidation.token(token, "za_")}", "Content-Type" to "multipart/form-data; boundary=$boundary", "Accept" to "application/json"), body.toByteArray(), 1024 * 1024))
        if (response.status != 201) throw SocialFailure(response.status)
        return message(StrictJson.parse(response.body, 16).obj())
    }
    suspend fun download(token: String, attachment: String): ByteArray {
        val response = transport.exchange(HttpCall("GET", scope.baseUri.toString() + "api/v1/social/attachments/${id(attachment)}", mapOf("Authorization" to "Bearer ${AccountValidation.token(token, "za_")}"), maxBytes = 25 * 1024 * 1024))
        if (response.status != 200) throw SocialFailure(response.status)
        return response.body
    }
    private suspend fun request(token: String, method: String, path: String, body: String? = null, expected: Int = 200): JsonValue {
        val headers = mutableMapOf("Accept" to "application/json", "Authorization" to "Bearer ${AccountValidation.token(token, "za_")}")
        if (body != null) headers["Content-Type"] = "application/json"
        val response = transport.exchange(HttpCall(method, scope.baseUri.toString() + "api/v1/social" + path, headers, body?.toByteArray(), 1024 * 1024))
        if (response.status != expected) throw SocialFailure(response.status)
        if (response.body.size > 1024 * 1024 || response.headers.any { it.key.equals("Content-Encoding", true) && it.value.isNotBlank() }) throw SocialFailure(502)
        return StrictJson.parse(response.body, 16)
    }
    private fun parseHome(obj: JsonValue.Obj): SocialHome = SocialHome(obj.text("code", 64), obj.array("friends", 1000).items.map {
        val row = it.obj()
        val unread = row.int("unread"); require(unread >= 0)
        InboxRow(id(row.text("conversationId", 36)), row.nullableText("displayName", 80)?.takeIf { it.isNotBlank() } ?: row.text("username", 80), lastBody = row.nullableText("lastBody", 4000), lastAt = row.nullableText("lastAt", 40)?.let(Instant::parse), unread = unread)
    }, invites(obj, "incoming"), invites(obj, "outgoing"))
    private fun invites(obj: JsonValue.Obj, key: String) = obj.array(key, 1000).items.map { val row = it.obj(); SocialInvite(id(row.text("friendshipId", 36)), row.nullableText("displayName", 80)?.takeIf { it.isNotBlank() } ?: row.text("username", 80)) }
    private fun message(row: JsonValue.Obj) = SocialMessage(id(row.text("messageId", 36)), id(row.text("senderId", 36)), row.text("senderName", 80), row.nullableText("body", 16000), row.text("kind", 24), Instant.parse(row.text("createdAt", 40)), row.nullableText("replyTo", 36)?.let(::id), row.nullableText("replyBody", 16000), row.bool("deleted"), row.nullableText("editedAt", 40) != null, row.bool("read"), row.array("reactions", 32).items.map { val r = it.obj(); SocialReaction(r.text("emoji", 32), r.int("count"), r.bool("mine")) }, row.nullableText("attachmentId", 36)?.let(::id), row.nullableText("fileName", 255),
        when(row.fields["durationMs"]) { null, JsonValue.Null -> null; else -> row.int("durationMs").also { require(it in 1..180_000) } })
    private fun id(value: String) = CommunityValidation.id(value)
    private fun textBody(body: String, reply: String?): String = "{\"body\":${quote(CommunityValidation.message(body))},\"replyTo\":${reply?.let { quote(id(it)) } ?: "null"}}"
    private fun quote(value: String): String = buildString { append('"'); value.forEach { c -> when(c) { '"' -> append("\\\""); '\\' -> append("\\\\"); '\n' -> append("\\n"); '\r' -> append("\\r"); '\t' -> append("\\t"); else -> if (c < ' ') append("\\u%04x".format(c.code)) else append(c) } }; append('"') }
}
