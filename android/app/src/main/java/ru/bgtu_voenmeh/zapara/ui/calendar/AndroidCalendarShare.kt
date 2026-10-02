package ru.bgtu_voenmeh.zapara.ui.calendar

import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import ru.bgtu_voenmeh.zapara.R
import java.io.File
import java.nio.charset.StandardCharsets
import java.util.UUID

object AndroidCalendarShare {
    fun writeIcs(context: Context, result: CalendarExportResult): Uri {
        require(result.eventCount > 0)
        val directory = File(context.cacheDir, "calendar-exports")
        check(directory.isDirectory || directory.mkdirs())
        val file = File(directory, "schedule-${UUID.randomUUID()}.ics")
        file.writeText(result.content, StandardCharsets.UTF_8)
        return FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", file)
    }

    fun icsIntent(context: Context, uri: Uri): Intent = Intent(Intent.ACTION_SEND).apply {
        type = "text/calendar"
        putExtra(Intent.EXTRA_STREAM, uri)
        clipData = ClipData.newUri(context.contentResolver,
            context.getString(R.string.ux300_android_share_schedule), uri)
        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    }

    fun textIntent(text: String): Intent = Intent(Intent.ACTION_SEND).apply {
        type = "text/plain"
        putExtra(Intent.EXTRA_TEXT, text)
    }
}
