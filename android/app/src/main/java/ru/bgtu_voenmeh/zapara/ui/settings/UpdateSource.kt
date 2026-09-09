package ru.bgtu_voenmeh.zapara.ui.settings

import java.io.File

data class UpdateInfo(val tag: String, val htmlUrl: String, val apkUrl: String?, val publishedAt: String)

data class CachedCheck(val at: Long, val tag: String?, val apkUrl: String?, val htmlUrl: String?)

interface UpdateSource {
    suspend fun latest(): UpdateInfo?
    suspend fun download(url: String, tag: String, onProgress: (Long, Long) -> Unit): File
    fun install(file: File)
    fun cached(): CachedCheck
    fun saveCheck(tag: String?, apkUrl: String?, htmlUrl: String?)
}
