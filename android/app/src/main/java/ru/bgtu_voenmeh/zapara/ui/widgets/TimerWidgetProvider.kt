package ru.bgtu_voenmeh.zapara.ui.widgets

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context

class TimerWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        WidgetUpdater.refresh(context)
    }

    override fun onEnabled(context: Context) {
        WidgetUpdater.refresh(context)
    }

    override fun onDisabled(context: Context) {
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
}
