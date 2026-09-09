package ru.bgtu_voenmeh.zapara

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import kotlinx.coroutines.runBlocking
import ru.bgtu_voenmeh.zapara.data.Notifications

class NotificationReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val pending = goAsync()
        Thread {
            try {
                restoreHost(context)
                Notifications.showForTime(context.applicationContext, intent.getStringExtra("time"))
            } catch (e: Exception) {
                Log.w("ZaparaNotify", "alarm", e)
            } finally {
                pending.finish()
            }
        }.start()
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        val pending = goAsync()
        Thread {
            try {
                restoreHost(context)
                Notifications.schedule(context.applicationContext)
            } catch (e: Exception) {
                Log.w("ZaparaNotify", "boot", e)
            } finally {
                pending.finish()
            }
        }.start()
    }
}

private fun restoreHost(context: Context) {
    val app = context.applicationContext as? ZaparaApplication ?: return
    runBlocking { app.host.restore() }
}
