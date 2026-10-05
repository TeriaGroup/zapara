package ru.bgtu_voenmeh.zapara.data.avatars

import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation
import ru.bgtu_voenmeh.zapara.data.api.*
import ru.bgtu_voenmeh.zapara.data.communities.CommunityValidation
import java.io.ByteArrayOutputStream
import java.util.UUID

enum class AvatarKind { User, Group }
data class AvatarTarget(val kind: AvatarKind, val id: String)
data class AvatarDownload(val bytes: ByteArray?, val etag: String?, val unchanged: Boolean = false)
class AvatarFailure(val status: Int) : Exception("Не удалось загрузить фото")

class AvatarHttpClient(private val transport: HttpExchange, private val scope: AccountServerScope) {
    suspend fun download(token: String, target: AvatarTarget, etag: String? = null): AvatarDownload {
        val headers = headers(token).apply {
            put("Accept", "image/webp")
            etag?.takeIf { it.length <= 128 && !it.contains('\r') && !it.contains('\n') }?.let { put("If-None-Match", it) }
        }
        val reply = transport.exchange(HttpCall("GET", url(target), headers, maxBytes = maxDownloadBytes, readTimeoutMs = 15_000))
        if (reply.status == 404) return AvatarDownload(null, null)
        if (reply.status == 304 && etag != null) return AvatarDownload(null, etag, unchanged = true)
        if (reply.status != 200) throw AvatarFailure(reply.status)
        if (reply.body.isEmpty() || reply.body.size > maxDownloadBytes ||
            reply.contentType?.substringBefore(';')?.trim() != "image/webp" ||
            reply.body.size < 12 || String(reply.body, 0, 4, Charsets.US_ASCII) != "RIFF" ||
            String(reply.body, 8, 4, Charsets.US_ASCII) != "WEBP") throw AvatarFailure(502)
        val revision = reply.headers.entries.firstOrNull { it.key.equals("ETag", true) }?.value
            ?.takeIf { it.length <= 128 && !it.contains('\r') && !it.contains('\n') }
        return AvatarDownload(reply.body, revision)
    }

    suspend fun upload(token: String, target: AvatarTarget, bytes: ByteArray): String {
        require(bytes.isNotEmpty() && bytes.size <= maxUploadBytes)
        val boundary = "avatar-${UUID.randomUUID()}"
        val output = ByteArrayOutputStream(bytes.size + 256)
        output.write(("--$boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"avatar.webp\"\r\n" +
            "Content-Type: application/octet-stream\r\n\r\n").toByteArray())
        output.write(bytes)
        output.write("\r\n--$boundary--\r\n".toByteArray())
        val reply = transport.exchange(HttpCall("PUT", editUrl(target), headers(token).apply {
            put("Content-Type", "multipart/form-data; boundary=$boundary")
        }, output.toByteArray(), maxBytes = 4096))
        if (reply.status != 200) throw AvatarFailure(reply.status)
        return CommunityValidation.id(StrictJson.parse(reply.body, 4).obj().text("revision", 36))
    }

    suspend fun remove(token: String, target: AvatarTarget) {
        val reply = transport.exchange(HttpCall("DELETE", editUrl(target), headers(token), maxBytes = 4096))
        if (reply.status != 204) throw AvatarFailure(reply.status)
    }

    private fun headers(token: String) = mutableMapOf("Authorization" to "Bearer ${AccountValidation.token(token, "za_")}", "Accept" to "application/json")
    private fun url(target: AvatarTarget): String = scope.baseUri.toString() + "api/v1/social/avatars/" +
        (if (target.kind == AvatarKind.User) "users/" else "groups/") + CommunityValidation.id(target.id)
    private fun editUrl(target: AvatarTarget): String = if (target.kind == AvatarKind.User)
        scope.baseUri.toString() + "api/v1/social/avatars/me" else url(target)

    companion object {
        const val maxUploadBytes = 3 * 1024 * 1024
        const val maxDownloadBytes = 512 * 1024
    }
}
