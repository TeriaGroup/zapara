package ru.bgtu_voenmeh.zapara.data

import java.io.ByteArrayOutputStream
import java.util.concurrent.TimeUnit

internal object SupportLogs {
    const val maxFiles = 3
    const val maxBytes = 512 * 1024

    fun collect(): List<Pair<String, ByteArray>> = pack(readLogcat())

    fun pack(text: String): List<Pair<String, ByteArray>> {
        val clean = text.replace("\u0000", "")
        if (clean.isEmpty()) return emptyList()
        return split(clean.toByteArray(Charsets.UTF_8)).mapIndexed { index, bytes ->
            "android-${index + 1}.log" to bytes
        }
    }

    private fun readLogcat(): String {
        val pid = android.os.Process.myPid().toString()
        try {
            return runLogcat(arrayOf("logcat", "-d", "-b", "main", "-b", "crash", "-v", "threadtime", "--pid=$pid"))
        } catch (_: Exception) {
            return try {
                runLogcat(arrayOf("logcat", "-d", "-b", "main", "-b", "crash", "-v", "threadtime"))
            } catch (_: Exception) {
                ""
            }
        }
    }

    private fun runLogcat(command: Array<String>): String {
        val process = ProcessBuilder(*command).redirectErrorStream(true).start()
        val window = ArrayDeque<ByteArray>()
        val kept = intArrayOf(0)
        val cap = maxFiles * maxBytes
        val lock = Any()
        val reader = Thread {
            try {
                val buf = ByteArray(8192)
                val input = process.inputStream
                while (true) {
                    val n = input.read(buf)
                    if (n < 0) break
                    val piece = buf.copyOf(n)
                    synchronized(lock) {
                        window.addLast(piece)
                        kept[0] += n
                        while (kept[0] > cap && window.size > 1) kept[0] -= window.removeFirst().size
                    }
                }
            } catch (_: Exception) {
            }
        }
        reader.isDaemon = true
        reader.start()
        try {
            if (!process.waitFor(4, TimeUnit.SECONDS)) process.destroy()
            reader.join(1000)
        } finally {
            process.destroy()
        }
        val tail = ByteArrayOutputStream()
        synchronized(lock) {
            for (piece in window) tail.write(piece)
        }
        return tail.toString(Charsets.UTF_8)
    }

    internal fun split(bytes: ByteArray): List<ByteArray> {
        if (bytes.isEmpty()) return emptyList()
        val budget = maxFiles * maxBytes
        var start = if (bytes.size > budget) bytes.size - budget else 0
        while (start < bytes.size && (bytes[start].toInt() and 0xC0) == 0x80) start++
        if (start >= bytes.size) return emptyList()
        if (start > 0) {
            var newline = start
            val window = minOf(bytes.size, start + 4096)
            while (newline < window && bytes[newline] != '\n'.code.toByte()) newline++
            if (newline < bytes.size && bytes[newline] == '\n'.code.toByte() && newline + 1 < bytes.size)
                start = newline + 1
        }
        val newestFirst = ArrayList<ByteArray>()
        var end = bytes.size
        while (end > start && newestFirst.size < maxFiles) {
            var chunkStart = maxOf(start, end - maxBytes)
            if (chunkStart > start) {
                while (chunkStart < end && (bytes[chunkStart].toInt() and 0xC0) == 0x80) chunkStart++
                var newline = chunkStart
                val window = minOf(end, chunkStart + 4096)
                while (newline < window && bytes[newline] != '\n'.code.toByte()) newline++
                if (newline < end && bytes[newline] == '\n'.code.toByte() && newline + 1 < end)
                    chunkStart = newline + 1
            }
            if (chunkStart >= end) break
            newestFirst.add(bytes.copyOfRange(chunkStart, end))
            end = chunkStart
        }
        newestFirst.reverse()
        return newestFirst
    }
}
