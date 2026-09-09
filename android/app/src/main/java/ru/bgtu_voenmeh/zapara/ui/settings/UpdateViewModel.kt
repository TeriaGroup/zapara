package ru.bgtu_voenmeh.zapara.ui.settings

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.io.File
import java.time.LocalTime

data class UpdateUiState(
    val checking: Boolean = false,
    val tag: String = "",
    val apkUrl: String? = null,
    val htmlUrl: String? = null,
    val hasUpdate: Boolean = false,
    val upToDate: Boolean = false,
    val error: String? = null,
    val downloading: Boolean = false,
    val progress: Float = -1f,
    val doneBytes: Long = 0L,
    val totalBytes: Long = -1L,
    val readyFile: String? = null,
    val auto: Boolean = true,
    val log: String = "",
    val checkedAt: String = ""
)

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
    @Volatile private var cancelled = false

    fun checkOnStart() {
        if (!autoEnabled()) return
        check(manual = false)
    }

    fun check(manual: Boolean) {
        if (mutable.value.checking || mutable.value.downloading) return
        scope.launch {
            mutable.update { it.copy(checking = true, error = null, upToDate = false, hasUpdate = false, log = copy.get("upd_log_request")) }
            try {
                val cached = source.cached()
                val info = if (!manual && cached.tag != null && System.currentTimeMillis() - cached.at < 6 * 3600_000L) {
                    UpdateInfo(cached.tag, cached.htmlUrl.orEmpty(), cached.apkUrl, "")
                } else {
                    source.latest()?.also { source.saveCheck(it.tag, it.apkUrl, it.htmlUrl) }
                }
                val at = stamp()
                if (info == null) {
                    mutable.update { it.copy(checking = false, upToDate = true, hasUpdate = false, error = null, log = copy.get("upd_log_none"), checkedAt = at) }
                } else if (isNewer(info.tag, currentTag)) {
                    mutable.update {
                        it.copy(
                            checking = false, tag = info.tag, apkUrl = info.apkUrl, htmlUrl = info.htmlUrl,
                            hasUpdate = true, upToDate = false, log = copy.get("upd_log_found", info.tag), checkedAt = at
                        )
                    }
                } else {
                    mutable.update { it.copy(checking = false, upToDate = true, hasUpdate = false, tag = info.tag, log = copy.get("upd_log_none"), checkedAt = at) }
                }
            } catch (e: Exception) {
                val raw = e.message ?: e.javaClass.simpleName
                val friendly = if ("403" in raw) copy.get("upd_err_403") else copy.get("upd_err", raw)
                mutable.update { it.copy(checking = false, error = friendly, log = copy.get("upd_log_fail"), checkedAt = stamp()) }
            }
        }
    }

    fun download() {
        val tag = mutable.value.tag
        val url = mutable.value.apkUrl ?: return
        if (tag.isEmpty() || mutable.value.downloading) return
        cancelled = false
        downloadJob?.cancel()
        downloadJob = scope.launch {
            mutable.update { it.copy(downloading = true, progress = -1f, error = null, log = copy.get("upd_log_connect")) }
            try {
                val file = source.download(url, tag) { done, total ->
                    val p = if (total > 0) done.toFloat() / total else -1f
                    mutable.update { it.copy(progress = p, doneBytes = done, totalBytes = total, log = copy.get("upd_log_dl")) }
                }
                mutable.update { it.copy(downloading = false, progress = 1f, readyFile = file.absolutePath, log = copy.get("upd_log_done")) }
            } catch (e: Exception) {
                mutable.update { it.copy(downloading = false, error = copy.get("upd_err_dl", e.message ?: e.javaClass.simpleName), log = copy.get("upd_log_dl_fail")) }
            }
        }
    }

    fun install() {
        val path = mutable.value.readyFile ?: return
        source.install(File(path))
        mutable.update { it.copy(log = copy.get("upd_log_install")) }
    }

    fun cancel() {
        cancelled = true
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
