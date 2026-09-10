package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import android.content.Intent
import androidx.core.content.FileProvider
import org.json.JSONArray
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL

object AutoUpdate {
    const val CURRENT_TAG = "android-v1.99"
    private const val OWNER = "TeriaGroup"
    private const val REPO = "zapara"
    private const val PREFS = "zapara"
    private const val KEY_AUTO = "auto_update"
    private const val KEY_CHECK_AT = "update_check_at"
    private const val KEY_CHECK_TAG = "update_check_tag"
    private const val KEY_CHECK_APK = "update_check_apk"
    private const val KEY_CHECK_HTML = "update_check_html"
    const val RELEASES_PAGE = "https://github.com/TeriaGroup/zapara/releases/latest"
    /** 6h: GitHub allows 60 anon API calls/hour per IP — VPNs share one IP, don't burn it. */
    const val CHECK_TTL_MS = 6 * 3600 * 1000L

    data class CachedCheck(val at: Long, val tag: String?, val apkUrl: String?, val htmlUrl: String?)

    fun cachedCheck(ctx: Context): CachedCheck {
        val p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        return CachedCheck(
            p.getLong(KEY_CHECK_AT, 0),
            p.getString(KEY_CHECK_TAG, null),
            p.getString(KEY_CHECK_APK, null),
            p.getString(KEY_CHECK_HTML, null)
        )
    }

    fun saveCheck(ctx: Context, tag: String?, apkUrl: String?, htmlUrl: String?) {
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putLong(KEY_CHECK_AT, System.currentTimeMillis())
            .putString(KEY_CHECK_TAG, tag)
            .putString(KEY_CHECK_APK, apkUrl)
            .putString(KEY_CHECK_HTML, htmlUrl)
            .apply()
    }

    data class UpdateInfo(val tag: String, val htmlUrl: String, val apkUrl: String?, val publishedAt: String)

    private const val FEED_URL = "https://github.com/$OWNER/$REPO/releases.atom"

    /**
     * Primary lookup: releases Atom feed (plain web traffic — NO API quota, VPN-proof).
     * Asset URLs are stable: .../releases/download/<tag>/<filename>.
     * Returns null when the feed is unreachable or the file 404s (caller falls back to API).
     */
    fun getLatestViaFeed(channel: String = "android"): UpdateInfo? {
        val pfx = if (channel == "windows") "windows-" else "android-"
        val assetName = if (channel == "windows") "ZAPARA_win-x64.zip" else "ZAPARA_android-debug.apk"
        val conn = (URL(FEED_URL).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            setRequestProperty("User-Agent", "Zapara-AutoUpdate/1.0")
            setRequestProperty("Cache-Control", "no-cache")
            connectTimeout = 10000; readTimeout = 15000
        }
        try {
            if (conn.responseCode !in 200..299) return null
            val xml = conn.inputStream.bufferedReader().readText()
            val tag = parseFeedTag(xml, pfx) ?: return null
            val apkUrl = "https://github.com/$OWNER/$REPO/releases/download/$tag/$assetName"
            if (!urlExists(apkUrl)) return null
            return UpdateInfo(tag, "https://github.com/$OWNER/$REPO/releases/tag/$tag", apkUrl, "")
        } finally {
            conn.disconnect()
        }
    }

    fun parseFeedTag(xml: String, prefix: String): String? {
        val re = Regex("href=\"[^\"]*/releases/tag/([^\"]+)\"")
        var best: String? = null
        for (m in re.findAll(xml)) {
            val tag = m.groupValues[1]
            if (!tagMatchesChannel(tag, prefix)) continue
            best = betterTag(best, tag, prefix)
        }
        return best
    }

    fun tagMatchesChannel(tag: String, prefix: String): Boolean =
        tag.startsWith(prefix, ignoreCase = true) ||
            tag.matches(Regex("""v\d[\d.]*""", RegexOption.IGNORE_CASE))

    private fun urlExists(url: String): Boolean {
        var c: HttpURLConnection? = null
        return try {
            c = (URL(url).openConnection() as HttpURLConnection).apply {
                requestMethod = "HEAD"
                setRequestProperty("User-Agent", "Zapara-AutoUpdate/1.0")
                connectTimeout = 10000; readTimeout = 10000
                instanceFollowRedirects = true
            }
            c.responseCode in 200..299
        } catch (_: Exception) {
            false
        } finally {
            c?.disconnect()
        }
    }

    /**
     * Smart lookup: feed first (no quota), API fallback (exact asset URLs, quota-limited).
     * Throws only when BOTH fail — with the API error (it carries the HTTP code).
     */
    fun getLatestSmart(channel: String = "android"): UpdateInfo? {
        try {
            getLatestViaFeed(channel)?.let { return it }
        } catch (_: Exception) {
        }
        return getLatest(channel)
    }

    fun getLatest(channel: String = "android"): UpdateInfo? {
        val pfx = if (channel == "windows") "windows-" else "android-"
        // Cache-buster: some networks/VPNs serve stale API responses without it.
        val url = URL("https://api.github.com/repos/$OWNER/$REPO/releases?per_page=100&t=${System.currentTimeMillis()}")
        val conn = (url.openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            setRequestProperty("User-Agent", "Zapara-AutoUpdate/1.0")
            setRequestProperty("Accept", "application/vnd.github+json")
            setRequestProperty("Cache-Control", "no-cache")
            connectTimeout = 8000; readTimeout = 8000
        }
        // Throw with the code (visible in UI) instead of silent null — null now means "no matching release".
        if (conn.responseCode !in 200..299) throw IOException("GitHub API: HTTP ${conn.responseCode}")
        val json = conn.inputStream.bufferedReader().readText()
        val arr = JSONArray(json)
        val wantExt = if (channel == "windows") ".zip" else ".apk"
        var best: UpdateInfo? = null
        for (i in 0 until arr.length()) {
            val o = arr.getJSONObject(i)
            val tag = o.getString("tag_name")
            if (!tagMatchesChannel(tag, pfx)) continue
            var apk: String? = null
            val assets = o.optJSONArray("assets")
            if (assets != null) {
                for (j in 0 until assets.length()) {
                    val a = assets.getJSONObject(j)
                    val name = a.getString("name")
                    if (name.endsWith(wantExt, ignoreCase = true)) {
                        apk = a.getString("browser_download_url")
                        if (name.contains("ZAPARA", ignoreCase = true)) break
                    }
                }
            }
            if (apk == null) continue
            val cand = UpdateInfo(tag, o.getString("html_url"), apk, o.optString("published_at", ""))
            if (best == null || betterTag(best.tag, tag, pfx) == tag) best = cand
        }
        return best
    }

    private fun betterTag(current: String?, candidate: String, prefix: String): String {
        if (current == null) return candidate
        if (isNewer(candidate, current)) return candidate
        if (isNewer(current, candidate)) return current
        val curPfx = current.startsWith(prefix, ignoreCase = true)
        val candPfx = candidate.startsWith(prefix, ignoreCase = true)
        return if (candPfx && !curPfx) candidate else current
    }

    class DownloadCancelled : IOException("cancelled")

    /** Download a release asset with progress. Throws on HTTP error or [DownloadCancelled]. */
    fun downloadAsset(
        url: String,
        dest: File,
        onProgress: (done: Long, total: Long) -> Unit,
        isCancelled: () -> Boolean
    ) {
        val conn = (URL(url).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            setRequestProperty("User-Agent", "Zapara-AutoUpdate/1.0")
            connectTimeout = 10000
            readTimeout = 30000
            instanceFollowRedirects = true
        }
        if (conn.responseCode !in 200..299) throw IOException("HTTP ${conn.responseCode}")
        val total = conn.contentLengthLong.takeIf { it > 0 } ?: -1L
        dest.parentFile?.mkdirs()
        val tmp = File(dest.parent, dest.name + ".part")
        try {
            conn.inputStream.use { inp ->
                FileOutputStream(tmp).use { out ->
                    val buf = ByteArray(8192)
                    var done = 0L
                    while (true) {
                        if (isCancelled()) throw DownloadCancelled()
                        val n = inp.read(buf)
                        if (n < 0) break
                        out.write(buf, 0, n)
                        done += n
                        onProgress(done, total)
                    }
                }
            }
            if (!tmp.renameTo(dest)) {
                tmp.copyTo(dest, overwrite = true)
                tmp.delete()
            }
        } catch (e: Exception) {
            try { tmp.delete() } catch (_: Exception) {}
            throw e
        } finally {
            conn.disconnect()
        }
    }

    fun apkFileFor(ctx: Context, tag: String): File =
        File(File(ctx.cacheDir, "updates"), "ZAPARA_${tag}_android.apk")

    /** System installer intent for a downloaded APK (still needs one user tap — OS requirement). */
    fun installIntent(ctx: Context, file: File): Intent {
        val uri = FileProvider.getUriForFile(ctx, "${ctx.packageName}.fileprovider", file)
        return Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
    }

    fun isAutoUpdateEnabled(ctx: Context): Boolean =
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getBoolean(KEY_AUTO, true)

    fun setAutoUpdateEnabled(ctx: Context, value: Boolean) {
        ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putBoolean(KEY_AUTO, value).apply()
    }

    fun isNewer(latest: String, current: String = CURRENT_TAG): Boolean {
        fun ver(t: String): String = when {
            "-v" in t -> t.substringAfter("-v")
            "-" in t -> t.substringAfter("-")
            else -> t
        }.trimStart('v','V')
        return try {
            val a = ver(latest).split(".").map { it.toIntOrNull() ?: 0 }
            val b = ver(current).split(".").map { it.toIntOrNull() ?: 0 }
            for (k in 0 until maxOf(a.size, b.size)) {
                val av = a.getOrElse(k){0}; val bv = b.getOrElse(k){0}
                if (av != bv) return av > bv
            }
            false
        } catch (_: Exception) { latest != current }
    }
}
