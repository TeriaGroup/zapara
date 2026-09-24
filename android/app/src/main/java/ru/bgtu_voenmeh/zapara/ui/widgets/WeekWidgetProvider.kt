package ru.bgtu_voenmeh.zapara.ui.widgets

import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.os.Bundle

class WeekWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, appWidgetManager: AppWidgetManager, appWidgetIds: IntArray) {
        WidgetUpdater.refresh(context)
    }

    override fun onAppWidgetOptionsChanged(
        context: Context, appWidgetManager: AppWidgetManager, appWidgetId: Int, newOptions: Bundle
    ) {
        WidgetUpdater.refresh(context)
    }

    override fun onDeleted(context: Context, appWidgetIds: IntArray) {
        WidgetUpdater.refresh(context)
    }

    override fun onDisabled(context: Context) {
        WidgetUpdater.refresh(context)
    }
}
