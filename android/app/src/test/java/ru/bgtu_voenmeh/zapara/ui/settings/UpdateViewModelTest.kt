package ru.bgtu_voenmeh.zapara.ui.settings

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import ru.bgtu_voenmeh.zapara.ui.XmlCopy
import java.io.File
import java.io.IOException

@OptIn(ExperimentalCoroutinesApi::class)
class UpdateViewModelTest {
    private val dispatcher = StandardTestDispatcher()

    @Before fun setMain() { Dispatchers.setMain(dispatcher) }
    @After fun reset() { Dispatchers.resetMain() }

    @Test fun check_newer_null_and_403() = runTest(dispatcher) {
        val source = FakeUpdateSource(latestResult = UpdateInfo("android-v2.1.0", "h", "u", "p"))
        val vm = UpdateViewModel(source, autoEnabled = { true }, isNewer = { latest, _ -> latest == "android-v2.1.0" }, scope = this, copy = XmlCopy)
        vm.check(manual = true)
        advanceUntilIdle()
        assertTrue(vm.state.value.hasUpdate)
        assertEquals("android-v2.1.0", vm.state.value.tag)

        source.latestResult = null
        vm.check(manual = true)
        advanceUntilIdle()
        assertTrue(vm.state.value.upToDate)

        source.latestError = IOException("HTTP 403")
        vm.check(manual = true)
        advanceUntilIdle()
        assertTrue(vm.state.value.error.orEmpty().startsWith("GitHub ограничил запросы"))
    }

    @Test fun download_install_and_auto_off() = runTest(dispatcher) {
        val file = File.createTempFile("zapara", ".apk")
        val source = FakeUpdateSource(
            latestResult = UpdateInfo("android-v2.1.0", "h", "http://apk", "p"),
            downloadFile = file
        )
        val vm = UpdateViewModel(source, autoEnabled = { true }, isNewer = { _, _ -> true }, scope = this, copy = XmlCopy)
        vm.check(manual = true)
        advanceUntilIdle()
        vm.download()
        advanceUntilIdle()
        assertEquals(file.absolutePath, vm.state.value.readyFile)
        assertEquals(1f, vm.state.value.progress)
        vm.install()
        assertEquals(1, source.installed.size)

        val quiet = FakeUpdateSource()
        val off = UpdateViewModel(quiet, autoEnabled = { false }, isNewer = { _, _ -> true }, scope = this, copy = XmlCopy)
        off.checkOnStart()
        advanceUntilIdle()
        assertEquals(0, quiet.latestCalls)
        assertNull(off.state.value.tag.takeIf { it.isNotEmpty() } ?: null)
    }
}

private class FakeUpdateSource(
    var latestResult: UpdateInfo? = null,
    var latestError: Exception? = null,
    var downloadFile: File = File("ready.apk")
) : UpdateSource {
    var latestCalls = 0
    val installed = mutableListOf<File>()
    override suspend fun latest(): UpdateInfo? {
        latestCalls++
        latestError?.let { throw it }
        return latestResult
    }
    override suspend fun download(url: String, tag: String, onProgress: (Long, Long) -> Unit): File {
        onProgress(0, 100)
        onProgress(100, 100)
        return downloadFile
    }
    override fun install(file: File) { installed += file }
    override fun cached(): CachedCheck = CachedCheck(0, null, null, null)
    override fun saveCheck(tag: String?, apkUrl: String?, htmlUrl: String?) {}
}
