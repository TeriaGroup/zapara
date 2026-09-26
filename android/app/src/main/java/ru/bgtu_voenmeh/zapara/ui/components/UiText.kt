package ru.bgtu_voenmeh.zapara.ui.components

import android.content.Context
import androidx.annotation.StringRes
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext

class UiText(private val context: Context) {
    operator fun invoke(@StringRes id: Int, vararg args: Any): String = context.getString(id, *args)
}

@Composable fun rememberUiText(): UiText = UiText(LocalContext.current)
