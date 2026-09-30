package ru.bgtu_voenmeh.zapara

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import kotlinx.coroutines.runBlocking
import ru.bgtu_voenmeh.zapara.data.Notifications
import ru.bgtu_voenmeh.zapara.ui.widgets.WidgetUpdater

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
        if (intent.action != Intent.ACTION_BOOT_COMPLETED && intent.action != Intent.ACTION_MY_PACKAGE_REPLACED) return
        val pending = goAsync()
        Thread {
            try {
                runStartupRecovery(
                    restoreProfile = { restoreHost(context) },
                    restartWidgets = { WidgetUpdater.reboot(context.applicationContext) },
                    restoreNotifications = { Notifications.schedule(context.applicationContext) },
                    onFailure = { step, error -> Log.w("ZaparaStartup", step, error) }
                )
            } finally {
                pending.finish()
            }
        }.start()
    }
}

internal fun runStartupRecovery(
    restoreProfile: () -> Unit,
    restartWidgets: () -> Unit,
    restoreNotifications: () -> Unit,
    onFailure: (String, Exception) -> Unit
) {
    for ((step, restore) in listOf(
        "profile" to restoreProfile,
        "widgets" to restartWidgets,
        "notifications" to restoreNotifications
    )) {
        try {
            restore()
        } catch (error: Exception) {
            onFailure(step, error)
        }
    }
}

private fun restoreHost(context: Context) {
    val app = context.applicationContext as? ZaparaApplication ?: return
    runBlocking { app.host.restore() }
}
