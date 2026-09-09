package ru.bgtu_voenmeh.zapara

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Rect
import android.graphics.Color
import android.view.View
import androidx.activity.ComponentActivity
import androidx.compose.ui.input.InputModeManager
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import java.io.ByteArrayOutputStream
import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import javax.xml.parsers.DocumentBuilderFactory
import javax.xml.transform.TransformerFactory
import javax.xml.transform.dom.DOMSource
import javax.xml.transform.stream.StreamResult

/** Test-only scoped runtime observations; never changes focus, input mode or product state. */
internal class ShellFocusDiagnostics(private val activity: ComponentActivity, private val mode: InputModeManager) {
    private val ins = InstrumentationRegistry.getInstrumentation()
    private val device = UiDevice.getInstance(ins)
    private val dir = requireNotNull(activity.getExternalFilesDir("shell-diagnostic"))
    private val retained = "/data/local/tmp/zapara-focus-direction"

    init {
        check(dir.exists() || dir.mkdirs())
        device.executeShellCommand("mkdir -p $retained")
    }

    fun trace(phase: String) {
        for (id in listOf("Focus.Primary", "Focus.Ghost")) {
            val node = device.findObject(By.res(id).pkg(activity.packageName))
            log("$phase $id focused=${node?.isFocused} focusable=${node?.isFocusable} clickable=${node?.isClickable}")
        }
        val focused = device.findObjects(By.pkg(activity.packageName).focused(true))
        log("$phase actualFocused=${focused.map { "${it.resourceName}:${it.className}" }}")
        ins.runOnMainSync {
            val decor = activity.window.decorView
            val root = activity.findViewById<View>(android.R.id.content)
            log("$phase lifecycle=${activity.lifecycle.currentState} windowFocus=${activity.hasWindowFocus()} token=${decor.windowToken} viewFocus=${decor.findFocus()?.javaClass?.name} mode=${mode.inputMode} touch=${decor.isInTouchMode} root=${root.width}x${root.height}")
        }
    }

    fun capture(name: String): File {
        val gate = CountDownLatch(1)
        ins.runOnMainSync { activity.window.decorView.postOnAnimation { activity.window.decorView.postOnAnimation { gate.countDown() } } }
        check(gate.await(5, TimeUnit.SECONDS)) { "Frame gate failed: $name" }
        val frame = Frames.capture(activity, "direction-$name")
        val bytes = ByteArrayOutputStream()
        device.dumpWindowHierarchy(bytes)
        val document = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(bytes.toByteArray().inputStream())
        val root = document.documentElement
        for (i in root.childNodes.length - 1 downTo 0) {
            val child = root.childNodes.item(i)
            if (child.nodeType == org.w3c.dom.Node.ELEMENT_NODE &&
                child.attributes.getNamedItem("package")?.nodeValue != activity.packageName) root.removeChild(child)
        }
        val hierarchy = File(dir, "$name.xml")
        TransformerFactory.newInstance().newTransformer().transform(DOMSource(document), StreamResult(hierarchy))
        device.executeShellCommand("cp ${frame.path} ${hierarchy.path} $retained/")
        log("FrameOutputPath=${frame.path} HierarchyOutputPath=${hierarchy.path}")
        return frame
    }

    fun compare(rest: File, focused: File, bounds: Rect, suffix: String, expectedColor: Int): Boolean {
        val location = IntArray(2)
        ins.runOnMainSync { activity.findViewById<View>(android.R.id.content).getLocationOnScreen(location) }
        val before = BitmapFactory.decodeFile(rest.path)
        val after = BitmapFactory.decodeFile(focused.path)
        var changed = 0
        var sampled = 0
        var outline = 0
        val density = activity.resources.displayMetrics.density
        // Only straight vertical edges: far from corner antialiasing and the 16dp text padding.
        val start = kotlin.math.ceil(4.5f * density).toInt()
        val end = kotlin.math.floor(5.5f * density).toInt()
        val adjacent = (9 * density).toInt()
        val perSide = mutableListOf<Int>()
        try {
            for (y in bounds.top - location[1] until bounds.bottom - location[1]) {
                for (x in bounds.left - location[0] until bounds.right - location[0]) {
                    if (before.getPixel(x, y) != after.getPixel(x, y)) changed++
                }
            }
            for (right in listOf(false, true)) {
                var sideOutline = 0
                for (y in bounds.height() / 4 until bounds.height() * 3 / 4) {
                    for (inset in start..end) {
                        val x = bounds.left - location[0] + if (right) bounds.width() - 1 - inset else inset
                        val insideX = bounds.left - location[0] + if (right) bounds.width() - 1 - adjacent else adjacent
                        val row = bounds.top - location[1] + y
                        val pixel = after.getPixel(x, row)
                        sampled++
                        if (closeColor(pixel, expectedColor) &&
                            contrast(pixel, before.getPixel(x, row)) >= 3.0 &&
                            contrast(pixel, after.getPixel(insideX, row)) >= 3.0) sideOutline++
                    }
                }
                perSide += sideOutline
                outline += sideOutline
            }
            for ((name, bitmap) in listOf("rest" to before, "focused" to after)) {
                val crop = Bitmap.createBitmap(bitmap, bounds.left - location[0], bounds.top - location[1], bounds.width(), bounds.height())
                val file = File(dir, "crop-$name-$suffix.png")
                try { file.outputStream().use { check(crop.compress(Bitmap.CompressFormat.PNG, 100, it)) } }
                finally { crop.recycle() }
                device.executeShellCommand("cp ${file.path} $retained/")
            }
        } finally { before.recycle(); after.recycle() }
        val passed = outline >= 24 * density * density && perSide.all { it >= sampled / 2 * 0.8 }
        log("$suffix bounds=$bounds contentOrigin=${location.toList()} buttonChangedPixels=$changed straightOutline=$outline/$sampled sides=$perSide contrastMin=3 expected=${Integer.toHexString(expectedColor)} PASS=$passed")
        return passed
    }

    private fun closeColor(a: Int, b: Int): Boolean =
        kotlin.math.abs(Color.red(a) - Color.red(b)) <= 18 &&
            kotlin.math.abs(Color.green(a) - Color.green(b)) <= 18 &&
            kotlin.math.abs(Color.blue(a) - Color.blue(b)) <= 18

    private fun contrast(a: Int, b: Int): Double {
        fun luminance(color: Int): Double {
            fun linear(channel: Int): Double {
                val value = channel / 255.0
                return if (value <= 0.04045) value / 12.92 else Math.pow((value + 0.055) / 1.055, 2.4)
            }
            return 0.2126 * linear(Color.red(color)) + 0.7152 * linear(Color.green(color)) + 0.0722 * linear(Color.blue(color))
        }
        val x = luminance(a); val y = luminance(b)
        return (maxOf(x, y) + 0.05) / (minOf(x, y) + 0.05)
    }

    companion object {
        fun log(message: String) { android.util.Log.i("ShellDirection", message) }
    }
}
