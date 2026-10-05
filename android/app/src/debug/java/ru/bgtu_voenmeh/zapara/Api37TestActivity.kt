package ru.bgtu_voenmeh.zapara

import android.os.Bundle
import android.util.Log
import androidx.activity.ComponentActivity
import java.util.UUID
import java.util.concurrent.CountDownLatch

/** Debug-only activity; its isolated task never contains MainActivity or user data. */
class Api37TestActivity : ComponentActivity() {
    val hostId: String = UUID.randomUUID().toString()
    val destroyed = CountDownLatch(1)

    private fun trace(event: String) {
        Log.i("Api37Host", "$hostId task=$taskId $event focus=${hasWindowFocus()} finishing=$isFinishing density=${resources.displayMetrics.density} fontScale=${resources.configuration.fontScale}")
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        title = "Api37Host-$hostId"
        trace("CREATE")
    }

    override fun onResume() { super.onResume(); trace("RESUME") }
    override fun onPause() { trace("PAUSE"); super.onPause() }
    override fun onStop() { trace("STOP"); super.onStop() }
    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        trace("FOCUS=$hasFocus")
    }

    override fun onDestroy() {
        super.onDestroy()
        trace("DESTROY")
        destroyed.countDown()
    }
}
