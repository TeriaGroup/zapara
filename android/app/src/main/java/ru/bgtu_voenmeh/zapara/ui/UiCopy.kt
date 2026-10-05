package ru.bgtu_voenmeh.zapara.ui

import android.content.Context
import androidx.compose.runtime.staticCompositionLocalOf

fun interface UiCopy {
    fun get(name: String, vararg args: Any?): String
}

class AndroidUiCopy(private val ctx: Context) : UiCopy {
    override fun get(name: String, vararg args: Any?): String {
        val id = ctx.resources.getIdentifier(name, "string", ctx.packageName)
        require(id != 0) { "missing string $name" }
        val values = args.map { it ?: "" }.toTypedArray()
        return if (values.isEmpty()) ctx.getString(id) else ctx.getString(id, *values)
    }
}

val LocalUiCopy = staticCompositionLocalOf<UiCopy> {
    error("LocalUiCopy")
}
