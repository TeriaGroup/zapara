package ru.bgtu_voenmeh.zapara.ui.components

import androidx.annotation.DrawableRes
import androidx.compose.runtime.Composable
import androidx.compose.ui.res.stringResource
import ru.bgtu_voenmeh.zapara.R

/**
 * #109 / AN-14: одно состояние «не вошли» для чатов и сообществ — заголовок, что доступно без входа
 * (подсказка экрана), основная кнопка «Войти».
 */
@Composable
fun SignedOutState(@DrawableRes icon: Int, hint: String, onSignIn: () -> Unit, tag: String) =
    EmptyState(icon, stringResource(R.string.signed_out_title), hint,
        actionText = stringResource(R.string.account_login), onAction = onSignIn, tag = tag)
