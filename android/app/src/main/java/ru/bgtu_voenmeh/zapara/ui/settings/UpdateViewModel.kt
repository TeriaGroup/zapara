package ru.bgtu_voenmeh.zapara.ui.settings

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.data.AutoUpdate
import java.io.File
import java.net.ConnectException
import java.net.NoRouteToHostException
import java.net.SocketException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.time.LocalTime
import javax.net.ssl.SSLException

data class UpdateUiState(
    val checking: Boolean = false,
    val tag: String = "",
    val apkUrl: String? = null,
    val htmlUrl: String? = null,
    val hasUpdate: Boolean = false,
    val updateStale: Boolean = false,
    val upToDate: Boolean = false,
    val error: String? = null,
    val downloading: Boolean = false,
    val progress: Float = -1f,
    val doneBytes: Long = 0L,
    val totalBytes: Long = -1L,
    val readyFile: String? = null,
    val readyTag: String? = null,
    val auto: Boolean = true,
    val log: String = "",
    val checkedAt: String = ""
) {
    val canInstall: Boolean get() = hasUpdate && readyTag == tag && readyFile?.let { File(it).isFile } == true
}

class UpdateViewModel(
    private val source: UpdateSource,
    private val autoEnabled: () -> Boolean,
    private val isNewer: (String, String) -> Boolean,
    private val currentTag: String = ru.bgtu_voenmeh.zapara.data.AutoUpdate.CURRENT_TAG,
    private val clock: () -> LocalTime = { LocalTime.now() },
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default),
    private val copy: ru.bgtu_voenmeh.zapara.ui.UiCopy
) {
    private val mutable = MutableStateFlow(UpdateUiState(auto = autoEnabled()))
    val state: StateFlow<UpdateUiState> = mutable.asStateFlow()
    private var downloadJob: Job? = null
    @Volatile private var downloadGeneration = 0L

    fun checkOnStart() {
        if (!autoEnabled()) return
        check(manual = false)
    }

    fun check(manual: Boolean) {
        if (mutable.value.checking || mutable.value.downloading) return
        mutable.update { it.copy(checking = true, error = null, upToDate = false, log = copy.get("upd_log_request")) }
        scope.launch {
            try {
                val cached = source.cached()
                // 6h: GitHub allows 60 anon API calls/hour per IP — VPNs share one IP, don't burn it.
                val info = if (!manual && cached.tag != null && cached.channel == source.channel() && System.currentTimeMillis() - cached.at < 6 * 3600_000L) {
                    UpdateInfo(cached.tag, cached.htmlUrl.orEmpty(), cached.apkUrl, "")
                } else {
                    source.latest()?.also { source.saveCheck(it.tag, it.apkUrl, it.htmlUrl) }
                }
                val at = stamp()
                if (info == null) {
                    mutable.update { it.copy(checking = false, upToDate = true, hasUpdate = false,
                        updateStale = false, readyFile = null, readyTag = null,
                        error = null, log = copy.get("upd_log_none"), checkedAt = at) }
                } else if (isNewer(info.tag, currentTag)) {
                    mutable.update {
                        it.copy(
                            checking = false, tag = info.tag, apkUrl = info.apkUrl, htmlUrl = info.htmlUrl,
                            hasUpdate = true, upToDate = false, updateStale = false,
                            readyFile = it.readyFile.takeIf { _ -> it.readyTag == info.tag },
                            readyTag = it.readyTag.takeIf { ready -> ready == info.tag },
                            log = copy.get("upd_log_found", info.tag), checkedAt = at
                        )
                    }
                } else {
                    mutable.update { it.copy(checking = false, upToDate = true, hasUpdate = false,
                        updateStale = false, readyFile = null, readyTag = null,
                        tag = info.tag, log = copy.get("upd_log_none"), checkedAt = at) }
                }
            } catch (e: CancellationException) {
                mutable.update { it.copy(checking = false) }
                throw e
            } catch (e: Exception) {
                val raw = e.message ?: e.javaClass.simpleName
                val friendly = when {
                    "ключ" in raw || "401" in raw -> copy.get("upd_err_token")
                    "403" in raw -> copy.get("upd_err_403")
                    e.isConnectionFailure() -> copy.get("load_fail_network")
                    else -> copy.get("upd_log_fail")
                }
                mutable.update { it.copy(checking = false, updateStale = it.hasUpdate,
                    error = friendly, log = copy.get("upd_log_fail"), checkedAt = stamp()) }
            }
        }
    }

    fun download() {
        val tag = mutable.value.tag
        val url = mutable.value.apkUrl ?: return
        if (tag.isEmpty() || !mutable.value.hasUpdate || mutable.value.checking || mutable.value.downloading) return
        val generation = ++downloadGeneration
        fun current() = generation == downloadGeneration && mutable.value.tag == tag
        mutable.update { it.copy(downloading = true, progress = -1f, error = null,
            readyFile = null, readyTag = null, log = copy.get("upd_log_connect")) }
        downloadJob?.cancel()
        downloadJob = scope.launch {
            try {
                val file = source.download(url, tag) { done, total ->
                    val p = if (total > 0) done.toFloat() / total else -1f
                    if (current()) mutable.update { state ->
                        if (generation == downloadGeneration && state.tag == tag)
                            state.copy(progress = p, doneBytes = done, totalBytes = total, log = copy.get("upd_log_dl"))
                        else state
                    }
                }
                if (!current()) return@launch
                mutable.update { state -> if (generation == downloadGeneration && state.tag == tag)
                    state.copy(downloading = false, progress = 1f, readyFile = file.absolutePath,
                        readyTag = tag, log = copy.get("upd_log_done")) else state }
            } catch (e: CancellationException) {
                throw e
            } catch (e: AutoUpdate.DownloadCancelled) {
                if (current()) mutable.update { state -> if (generation == downloadGeneration && state.tag == tag)
                    state.copy(downloading = false, log = copy.get("upd_log_cancel")) else state }
            } catch (e: Exception) {
                if (current()) {
                    mutable.update { state -> if (generation == downloadGeneration && state.tag == tag)
                        state.copy(downloading = false,
                            error = if (e.isConnectionFailure()) copy.get("load_fail_network") else copy.get("upd_log_dl_fail"),
                            log = copy.get("upd_log_dl_fail")) else state }
                }
            }
        }
    }

    fun install() {
        val current = mutable.value
        val path = current.readyFile ?: return
        if (!current.canInstall) {
            mutable.update { it.copy(readyFile = null, readyTag = null,
                error = copy.get("ux60_update_file_missing")) }
            return
        }
        try {
            source.install(File(path))
            mutable.update { it.copy(log = copy.get("upd_log_install"), error = null) }
        } catch (e: Exception) {
            mutable.update { it.copy(error = copy.get("ux60_update_install_failed"),
                log = copy.get("upd_log_dl_fail")) }
        }
    }

    fun cancel() {
        downloadGeneration++
        source.cancelDownload()
        downloadJob?.cancel()
        mutable.update { it.copy(downloading = false, log = copy.get("upd_log_cancel")) }
    }

    fun setAuto(value: Boolean) {
        mutable.update { it.copy(auto = value) }
    }

    private fun stamp(): String = try {
        clock().format(java.time.format.DateTimeFormatter.ofPattern("HH:mm"))
    } catch (_: Exception) {
        ""
    }
}

private fun Throwable.isConnectionFailure(): Boolean =
    generateSequence(this) { it.cause }.any { cause ->
        cause is UnknownHostException || cause is ConnectException || cause is NoRouteToHostException ||
            cause is SocketTimeoutException || cause is SocketException || cause is SSLException
    }
