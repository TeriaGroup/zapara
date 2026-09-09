package ru.bgtu_voenmeh.zapara.ui.account

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.PasswordVisualTransformation
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara

data class AccountUiState(
    val ready: Boolean = false,
    val busy: Boolean = false,
    val configured: Boolean = false,
    val guest: Boolean = true,
    val registration: Boolean = false,
    val registrationAvailable: Boolean = false,
    val username: String = "",
    val password: String = "",
    val displayName: String = "",
    val accountName: String = "",
    val status: String = "",
    val confirmLogout: Boolean = false
)

sealed interface AccountEvent {
    data class Username(val value: String) : AccountEvent
    data class Password(val value: String) : AccountEvent
    data class DisplayName(val value: String) : AccountEvent
    data object ToggleRegistration : AccountEvent
    data object Submit : AccountEvent
    data object RequestLogout : AccountEvent
    data object ConfirmLogout : AccountEvent
    data object CancelLogout : AccountEvent
}

@Composable
fun AccountCard(state: AccountUiState, onEvent: (AccountEvent) -> Unit) {
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth().testTag("Account.Card")) {
        Text(stringResource(R.string.account_title), style = Zapara.typography.section, color = c.text1)
        Text(state.status, style = Zapara.typography.body, color = c.text1, modifier = Modifier.testTag("Account.Status"))
        Text(stringResource(R.string.account_isolation), style = Zapara.typography.caption, color = c.text2)
        if (!state.configured || !state.ready) return@ZCard
        if (state.guest) {
            AccountField(state.username, stringResource(R.string.account_username), "Account.Username") {
                onEvent(AccountEvent.Username(it))
            }
            AccountField(state.password, stringResource(R.string.account_password), "Account.Password", password = true) {
                onEvent(AccountEvent.Password(it))
            }
            if (state.registration) {
                AccountField(state.displayName, stringResource(R.string.account_display_name), "Account.DisplayName") {
                    onEvent(AccountEvent.DisplayName(it))
                }
            }
            Text(stringResource(R.string.account_validation), style = Zapara.typography.caption, color = c.text2)
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.registration) {
                    ZButton(stringResource(R.string.account_register), { onEvent(AccountEvent.Submit) }, enabled = !state.busy, tag = "Account.Register")
                } else {
                    ZButton(stringResource(R.string.account_login), { onEvent(AccountEvent.Submit) }, enabled = !state.busy, tag = "Account.Login")
                }
                if (state.registrationAvailable) {
                    ZButton(stringResource(R.string.account_mode), { onEvent(AccountEvent.ToggleRegistration) }, ghost = true, enabled = !state.busy, tag = "Account.Mode")
                }
            }
        } else {
            Text(state.accountName, style = Zapara.typography.section, color = c.text1, modifier = Modifier.testTag("Account.Name"))
            if (!state.confirmLogout) {
                ZButton(stringResource(R.string.account_logout), { onEvent(AccountEvent.RequestLogout) }, ghost = true, enabled = !state.busy, tag = "Account.Logout")
            } else {
                Text(stringResource(R.string.account_confirm_logout), style = Zapara.typography.body, color = c.text1)
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.account_logout), { onEvent(AccountEvent.ConfirmLogout) }, enabled = !state.busy, tag = "Account.ConfirmLogout")
                    ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelLogout) }, ghost = true, tag = "Account.CancelLogout")
                }
            }
        }
    }
}

@Composable
private fun AccountField(value: String, label: String, tag: String, password: Boolean = false, onChange: (String) -> Unit) {
    val c = Zapara.colors
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        modifier = Modifier.fillMaxWidth().testTag(tag),
        label = { Text(label, style = Zapara.typography.caption) },
        singleLine = true,
        visualTransformation = if (password) PasswordVisualTransformation() else androidx.compose.ui.text.input.VisualTransformation.None,
        shape = RoundedCornerShape(Zapara.radii.control),
        colors = OutlinedTextFieldDefaults.colors(
            focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
            focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
            focusedTextColor = c.text1, unfocusedTextColor = c.text1
        )
    )
}
