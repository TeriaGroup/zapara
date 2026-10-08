package ru.bgtu_voenmeh.zapara.ui.settings

import android.content.Context
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import java.io.File

class GitHubUpdateSource(private val ctx: Context) : UpdateSource {
    @Volatile private var cancelled = false

    override fun channel(): String = AutoUpdate.channel(ctx)

    override suspend fun latest(): UpdateInfo? {
        val selected = AutoUpdate.channel(ctx)
        val token = AutoUpdate.token(ctx).takeIf { selected == AutoUpdate.CHANNEL_ALPHA }
        val info = AutoUpdate.getLatestSmart("android", AutoUpdate.repo(selected), token) ?: return null
        return UpdateInfo(info.tag, info.htmlUrl, info.apkUrl, info.publishedAt)
    }

    override suspend fun download(url: String, tag: String, onProgress: (Long, Long) -> Unit): File {
        cancelled = false
        val dest = AutoUpdate.apkFileFor(ctx, tag)
        if (!dest.exists()) {
            val token = AutoUpdate.token(ctx).takeIf { AutoUpdate.channel(ctx) == AutoUpdate.CHANNEL_ALPHA }
            AutoUpdate.downloadAsset(url, dest, onProgress, { cancelled }, token)
        }
        return dest
    }

    override fun cancelDownload() {
        cancelled = true
    }

    override fun install(file: File) {
        ctx.startActivity(AutoUpdate.installIntent(ctx, file))
    }

    override fun cached(): CachedCheck {
        val c = AutoUpdate.cachedCheck(ctx)
        return CachedCheck(c.at, c.tag, c.apkUrl, c.htmlUrl, c.channel)
    }

    override fun saveCheck(tag: String?, apkUrl: String?, htmlUrl: String?) {
        AutoUpdate.saveCheck(ctx, tag, apkUrl, htmlUrl)
    }
}
