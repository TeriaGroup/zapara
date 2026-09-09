package ru.bgtu_voenmeh.zapara

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.View
import androidx.activity.ComponentActivity
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.ComposeContentTestRule
import androidx.compose.ui.test.onRoot
import androidx.test.platform.app.InstrumentationRegistry
import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

object Frames {
    /**
     * Composited display pixels cropped to the activity content view.
     * PixelCopy.request(Window) returns ERROR_SOURCE_NO_DATA on this API 37 emulator
     * (empty window SurfaceControl). View.draw skips Compose graphicsLayer clips, so
     * focus strokes and ripples would be missing. Call from the instrumentation thread.
     */
    fun capture(activity: ComponentActivity, name: String): File {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        waitUntilLaidOut(activity)
        waitForPresentedFrame(activity)
        var originX = 0
        var originY = 0
        var width = 0
        var height = 0
        instrumentation.runOnMainSync {
            val root = activity.findViewById<View>(android.R.id.content)
            val location = IntArray(2)
            root.getLocationOnScreen(location)
            originX = location[0]
            originY = location[1]
            width = root.width
            height = root.height
        }
        check(width > 0 && height > 0) { "View not laid out for capture" }
        val full = instrumentation.uiAutomation.takeScreenshot()
            ?: error("UiAutomation.takeScreenshot returned null")
        try {
            val content = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(content)
            when {
                full.width == width && full.height == height -> canvas.drawBitmap(full, 0f, 0f, null)
                full.width >= originX + width && full.height >= originY + height ->
                    canvas.drawBitmap(full, -originX.toFloat(), -originY.toFloat(), null)
                else -> error(
                    "Screenshot ${full.width}x${full.height} cannot map content at ($originX,$originY) ${width}x${height}"
                )
            }
            return save(content, name)
        } finally {
            full.recycle()
        }
    }

    private fun waitUntilLaidOut(activity: ComponentActivity) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val deadline = System.currentTimeMillis() + 8_000
        while (System.currentTimeMillis() < deadline) {
            var ready = false
            instrumentation.runOnMainSync {
                val root = activity.findViewById<View>(android.R.id.content)
                ready = root.width > 0 && root.height > 0 && root.isShown
            }
            if (ready) return
            Thread.sleep(50)
        }
    }

    private fun waitForPresentedFrame(activity: ComponentActivity) {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val gate = CountDownLatch(1)
        instrumentation.runOnMainSync {
            activity.window.decorView.postOnAnimation {
                activity.window.decorView.postOnAnimation { gate.countDown() }
            }
        }
        check(gate.await(5, TimeUnit.SECONDS)) { "Frame gate failed" }
    }

    fun capture(rule: ComposeContentTestRule, name: String): File {
        require(name.matches(Regex("[a-z0-9-]+")))
        rule.waitForIdle()
        val bitmap = rule.onRoot().captureToImage().asAndroidBitmap()
        return save(bitmap, name)
    }

    private fun save(bitmap: Bitmap, name: String): File {
        require(name.matches(Regex("[a-z0-9-]+")))
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val dir = requireNotNull(context.getExternalFilesDir("frames"))
        check(dir.exists() || dir.mkdirs())
        return File(dir, "$name.png").also { file ->
            file.outputStream().use { check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, it)) }
        }
    }
}
