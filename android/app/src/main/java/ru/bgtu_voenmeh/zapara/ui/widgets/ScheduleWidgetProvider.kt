package ru.bgtu_voenmeh.zapara.ui.widgets

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.content.Intent

class ScheduleWidgetProvider : AppWidgetProvider() {
    override fun onDeleted(context: Context, appWidgetIds: IntArray) {
        WidgetUpdater.refresh(context)
    }

    override fun onDisabled(context: Context) {
        WidgetUpdater.refresh(context)
    }

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == ACTION_ADVANCE) {
            WidgetUpdater.refresh(context)
            return
        }
        if (intent.action == ACTION_REBOOT) {
            WidgetUpdater.reboot(context)
            return
        }
        if (intent.action == ACTION_HEARTBEAT) {
            WidgetUpdater.beat(context)
            return
        }
        super.onReceive(context, intent)
    }

    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        WidgetUpdater.refresh(context)
    }

    override fun onAppWidgetOptionsChanged(
        context: Context,
        appWidgetManager: AppWidgetManager,
        appWidgetId: Int,
        newOptions: android.os.Bundle
    ) {
        WidgetUpdater.refresh(context)
    }

    companion object {
        const val ACTION_ADVANCE = "ru.zapara.app.WIDGET_ADVANCE"
        const val ACTION_REBOOT = "ru.zapara.app.WIDGET_DAILY_REBOOT"
        const val ACTION_HEARTBEAT = "ru.zapara.app.WIDGET_HEARTBEAT"
        const val REQUEST = 4103
        const val DAILY = 4108
        const val HEARTBEAT = 4109
    }
}
