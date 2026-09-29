package ru.bgtu_voenmeh.zapara.data.avatars

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/** Memory only, owned by one account/server profile. Never shares private thumbnails with a guest. */
class AvatarStore(
    val userId: String,
    private val client: AvatarHttpClient,
    private val accessToken: suspend () -> String?,
    private val active: () -> Boolean = { true },
    private val clock: () -> Long = { System.currentTimeMillis() }
) {
    private data class Cached(val bytes: ByteArray?, val etag: String?, val at: Long)
    private val cache = LinkedHashMap<AvatarTarget, Cached>(16, .75f, true)
    private val downloads = Mutex()
    private val edits = Mutex()
    private val mutableRevision = MutableStateFlow(0L)
    val revision = mutableRevision.asStateFlow()
    @Volatile private var closed = false

    suspend fun load(target: AvatarTarget): ByteArray? = downloads.withLock {
        if (closed || !active()) return@withLock null
        val token = accessToken() ?: run { clear(); return@withLock null }
        val (epoch, old) = synchronized(cache) { mutableRevision.value to cache[target] }
        if (old != null && clock() - old.at in 0..45_000) return@withLock synchronized(cache) {
            if (closed || !active() || epoch != mutableRevision.value) null else old.bytes
        }
        try {
            val fetched = client.download(token, target, old?.etag)
            val entry = if (fetched.unchanged) Cached(old?.bytes, old?.etag, clock())
                else Cached(fetched.bytes, fetched.etag, clock())
            synchronized(cache) {
                if (closed || !active() || epoch != mutableRevision.value) null else {
                    cache[target] = entry
                    while (cache.size > 48) cache.remove(cache.keys.first())
                    entry.bytes
                }
            }
        } catch (cancelled: CancellationException) { throw cancelled }
        catch (failure: AvatarFailure) {
            if (failure.status in setOf(401, 403)) { clear(); null }
            else synchronized(cache) { if (!closed && active() && epoch == mutableRevision.value) old?.bytes else null }
        } catch (_: Exception) {
            synchronized(cache) { if (!closed && active() && epoch == mutableRevision.value) old?.bytes else null }
        }
    }

    suspend fun upload(target: AvatarTarget, bytes: ByteArray) = edits.withLock {
        requireEditable(target)
        val token = accessToken() ?: throw AvatarFailure(401)
        requireEditable(target)
        client.upload(token, target, bytes)
        if (!closed && active()) invalidate(target)
    }

    suspend fun remove(target: AvatarTarget) = edits.withLock {
        requireEditable(target)
        val token = accessToken() ?: throw AvatarFailure(401)
        requireEditable(target)
        client.remove(token, target)
        if (!closed && active()) invalidate(target)
    }

    private fun requireEditable(target: AvatarTarget) {
        if (closed || !active() || target.kind == AvatarKind.User && target.id != userId) throw AvatarFailure(403)
    }
    fun invalidate(target: AvatarTarget) {
        synchronized(cache) { cache.remove(target); mutableRevision.update { it + 1 } }
    }
    fun clear() {
        synchronized(cache) {
            if (cache.isNotEmpty()) { cache.clear(); mutableRevision.update { it + 1 } }
        }
    }
    fun close() = synchronized(cache) {
        closed = true
        cache.clear()
        mutableRevision.update { it + 1 }
    }
}
