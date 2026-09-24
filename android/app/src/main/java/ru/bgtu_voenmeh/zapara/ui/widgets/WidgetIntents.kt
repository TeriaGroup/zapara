package ru.bgtu_voenmeh.zapara.ui.widgets

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.net.Uri
import ru.bgtu_voenmeh.zapara.MainActivity

object WidgetIntents {
    fun open(context: Context, widgetId: Int, slot: Int, section: String, argument: String?): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            data = Uri.parse("zapara-widget://open/$widgetId/$slot")
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            putExtra(MainActivity.SECTION_EXTRA, section)
            putExtra(MainActivity.ARGUMENT_EXTRA, argument)
        }
        return PendingIntent.getActivity(context, 0, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }
}
