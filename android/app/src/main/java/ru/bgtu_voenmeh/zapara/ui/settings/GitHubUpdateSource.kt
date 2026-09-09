package ru.bgtu_voenmeh.zapara.ui.settings

import android.content.Context
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import java.io.File

class GitHubUpdateSource(private val ctx: Context) : UpdateSource {
    override suspend fun latest(): UpdateInfo? {
        val info = AutoUpdate.getLatestSmart("android") ?: return null
        return UpdateInfo(info.tag, info.htmlUrl, info.apkUrl, info.publishedAt)
    }

    override suspend fun download(url: String, tag: String, onProgress: (Long, Long) -> Unit): File {
        val dest = AutoUpdate.apkFileFor(ctx, tag)
        if (!dest.exists()) {
            AutoUpdate.downloadAsset(url, dest, onProgress) { false }
        }
        return dest
    }

    override fun install(file: File) {
        ctx.startActivity(AutoUpdate.installIntent(ctx, file))
    }

    override fun cached(): CachedCheck {
        val c = AutoUpdate.cachedCheck(ctx)
        return CachedCheck(c.at, c.tag, c.apkUrl, c.htmlUrl)
    }

    override fun saveCheck(tag: String?, apkUrl: String?, htmlUrl: String?) {
        AutoUpdate.saveCheck(ctx, tag, apkUrl, htmlUrl)
    }
}
