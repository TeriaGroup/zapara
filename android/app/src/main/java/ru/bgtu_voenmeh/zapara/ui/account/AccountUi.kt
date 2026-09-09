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
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64

data class AccountDeviceRow(
    val familyId: String,
    val deviceId: String,
    val deviceName: String,
    val platform: String,
    val current: Boolean
)

data class AccountIdentityRow(val provider: String)

data class AccountUiCapabilities(
    val registration: Boolean = false,
    val vk: Boolean = false,
    val yandex: Boolean = false,
    val recovery: Boolean = false
)

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
    val confirmLogout: Boolean = false,
    val currentPassword: String = "",
    val newPassword: String = "",
    val proof: String = "",
    val recoveryUsername: String = "",
    val devices: List<AccountDeviceRow> = emptyList(),
    val identities: List<AccountIdentityRow> = emptyList(),
    val exportReady: Boolean = false,
    val confirmDelete: Boolean = false,
    val vkAvailable: Boolean = false,
    val yandexAvailable: Boolean = false,
    val recoveryAvailable: Boolean = false
) {
    val showGuestAuth get() = configured && ready && guest
    val showAccount get() = configured && ready && !guest
    val showVkLogin get() = showGuestAuth && vkAvailable
    val showYandexLogin get() = showGuestAuth && yandexAvailable
    val showRecovery get() = showGuestAuth && recoveryAvailable
    val showDevices get() = showAccount
    val showPasswordChange get() = showAccount
    val showExport get() = showAccount
    val showDelete get() = showAccount
    val showIdentities get() = showAccount && (vkAvailable || yandexAvailable || identities.isNotEmpty())
    val showVkLink get() = showAccount && vkAvailable && identities.none { it.provider == "vk" }
    val showYandexLink get() = showAccount && yandexAvailable && identities.none { it.provider == "yandex" }
    val showVkUnlink get() = showAccount && identities.any { it.provider == "vk" }
    val showYandexUnlink get() = showAccount && identities.any { it.provider == "yandex" }

    fun clearSecrets() = copy(password = "", currentPassword = "", newPassword = "", proof = "")

    fun applyCaps(caps: AccountUiCapabilities) = copy(
        registrationAvailable = caps.registration,
        vkAvailable = caps.vk,
        yandexAvailable = caps.yandex,
        recoveryAvailable = caps.recovery
    )

    fun reduce(event: AccountEvent): AccountUiState = when (event) {
        is AccountEvent.Username -> copy(username = event.value)
        is AccountEvent.Password -> copy(password = event.value)
        is AccountEvent.DisplayName -> copy(displayName = event.value)
        is AccountEvent.CurrentPassword -> copy(currentPassword = event.value)
        is AccountEvent.NewPassword -> copy(newPassword = event.value)
        is AccountEvent.Proof -> copy(proof = event.value)
        is AccountEvent.RecoveryUsername -> copy(recoveryUsername = event.value)
        AccountEvent.ToggleRegistration ->
            if (!registrationAvailable) this else copy(registration = !registration, password = "")
        AccountEvent.RequestLogout -> copy(confirmLogout = true).clearSecrets()
        AccountEvent.CancelLogout -> copy(confirmLogout = false)
        AccountEvent.RequestDelete -> copy(confirmDelete = true)
        AccountEvent.CancelDelete -> copy(confirmDelete = false).clearSecrets()
        else -> this
    }
}

sealed interface AccountEvent {
    data class Username(val value: String) : AccountEvent
    data class Password(val value: String) : AccountEvent
    data class DisplayName(val value: String) : AccountEvent
    data class CurrentPassword(val value: String) : AccountEvent
    data class NewPassword(val value: String) : AccountEvent
    data class Proof(val value: String) : AccountEvent
    data class RecoveryUsername(val value: String) : AccountEvent
    data object ToggleRegistration : AccountEvent
    data object Submit : AccountEvent
    data object RequestLogout : AccountEvent
    data object ConfirmLogout : AccountEvent
    data object CancelLogout : AccountEvent
    data object LoadDevices : AccountEvent
    data class Revoke(val familyId: String) : AccountEvent
    data object ChangePassword : AccountEvent
    data object CreateExport : AccountEvent
    data object DownloadExport : AccountEvent
    data object RequestDelete : AccountEvent
    data object ConfirmDelete : AccountEvent
    data object CancelDelete : AccountEvent
    data object RequestReset : AccountEvent
    data object ConfirmReset : AccountEvent
    data object StartVk : AccountEvent
    data object StartYandex : AccountEvent
    data object LinkVk : AccountEvent
    data object LinkYandex : AccountEvent
    data class Unlink(val provider: String) : AccountEvent
}

internal data class NativePkce(val challenge: String, val verifier: String)

internal fun nativePkce(): NativePkce {
    val verifierBytes = ByteArray(32)
    SecureRandom().nextBytes(verifierBytes)
    val verifier = Base64.getUrlEncoder().withoutPadding().encodeToString(verifierBytes)
    val digest = MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII))
    val challenge = Base64.getUrlEncoder().withoutPadding().encodeToString(digest)
    return NativePkce(challenge, verifier)
}

internal suspend fun readUiCapabilities(transport: HttpExchange, baseUri: String): AccountUiCapabilities {
    val root = baseUri.trimEnd('/') + "/"
    val reply = transport.exchange(
        HttpCall("GET", root + "api/v1/auth/capabilities", linkedMapOf("Accept" to "application/json"), maxBytes = 65536)
    )
    if (reply.status != 200) throw JsonFail()
    val obj = StrictJson.parse(reply.body).obj()
    fun flag(name: String): Boolean = (obj.fields[name] as? JsonValue.Bool)?.value ?: false
    return AccountUiCapabilities(flag("registration"), flag("vk"), flag("yandex"), flag("recovery"))
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
            if (state.showVkLogin) {
                ZButton(stringResource(R.string.account_vk), { onEvent(AccountEvent.StartVk) }, ghost = true, enabled = !state.busy, tag = "Account.Vk")
            }
            if (state.showYandexLogin) {
                ZButton(stringResource(R.string.account_yandex), { onEvent(AccountEvent.StartYandex) }, ghost = true, enabled = !state.busy, tag = "Account.Yandex")
            }
            if (state.showRecovery) {
                AccountField(state.recoveryUsername, stringResource(R.string.account_recovery_email), "Account.Recovery") {
                    onEvent(AccountEvent.RecoveryUsername(it))
                }
                AccountField(state.proof, stringResource(R.string.account_proof), "Account.Proof", password = true) {
                    onEvent(AccountEvent.Proof(it))
                }
                AccountField(state.newPassword, stringResource(R.string.account_new_password), "Account.NewPassword", password = true) {
                    onEvent(AccountEvent.NewPassword(it))
                }
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.account_reset), { onEvent(AccountEvent.RequestReset) }, enabled = !state.busy, tag = "Account.Reset")
                    ZButton(stringResource(R.string.account_reset), { onEvent(AccountEvent.ConfirmReset) }, ghost = true, enabled = !state.busy, tag = "Account.ConfirmReset")
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
            AccountLifecyclePanel(state, onEvent)
        }
    }
}

@Composable
private fun AccountLifecyclePanel(state: AccountUiState, onEvent: (AccountEvent) -> Unit) {
    val enabled = !state.busy
    ZButton(stringResource(R.string.account_devices), { onEvent(AccountEvent.LoadDevices) }, ghost = true, enabled = enabled, tag = "Account.Devices")
    state.devices.forEach { device ->
        Text(device.deviceName, style = Zapara.typography.body, color = Zapara.colors.text1, modifier = Modifier.testTag("Account.Device"))
        ZButton(stringResource(R.string.account_revoke), { onEvent(AccountEvent.Revoke(device.familyId)) }, ghost = true, enabled = enabled, tag = "Account.Revoke")
    }
    AccountField(state.currentPassword, stringResource(R.string.account_current_password), "Account.CurrentPassword", password = true) {
        onEvent(AccountEvent.CurrentPassword(it))
    }
    AccountField(state.newPassword, stringResource(R.string.account_new_password), "Account.NewPassword", password = true) {
        onEvent(AccountEvent.NewPassword(it))
    }
    ZButton(stringResource(R.string.account_change_password), { onEvent(AccountEvent.ChangePassword) }, enabled = enabled, tag = "Account.ChangePassword")
    AccountField(state.proof, stringResource(R.string.account_proof), "Account.Proof", password = true) {
        onEvent(AccountEvent.Proof(it))
    }
    ZButton(stringResource(R.string.account_export), { onEvent(AccountEvent.CreateExport) }, ghost = true, enabled = enabled, tag = "Account.Export")
    if (state.exportReady) {
        ZButton(stringResource(R.string.account_export_download), { onEvent(AccountEvent.DownloadExport) }, enabled = enabled, tag = "Account.ExportDownload")
    }
    if (state.showIdentities) {
        Text(stringResource(R.string.account_identities), style = Zapara.typography.section, color = Zapara.colors.text1)
        if (state.showVkLink || state.showVkUnlink) {
            Text(stringResource(R.string.account_vk), style = Zapara.typography.body, color = Zapara.colors.text1)
            if (state.showVkLink) {
                ZButton(stringResource(R.string.account_link), { onEvent(AccountEvent.LinkVk) }, ghost = true, enabled = enabled, tag = "Account.LinkVk")
            }
            if (state.showVkUnlink) {
                ZButton(stringResource(R.string.account_unlink), { onEvent(AccountEvent.Unlink("vk")) }, ghost = true, enabled = enabled, tag = "Account.UnlinkVk")
            }
        }
        if (state.showYandexLink || state.showYandexUnlink) {
            Text(stringResource(R.string.account_yandex), style = Zapara.typography.body, color = Zapara.colors.text1)
            if (state.showYandexLink) {
                ZButton(stringResource(R.string.account_link), { onEvent(AccountEvent.LinkYandex) }, ghost = true, enabled = enabled, tag = "Account.LinkYandex")
            }
            if (state.showYandexUnlink) {
                ZButton(stringResource(R.string.account_unlink), { onEvent(AccountEvent.Unlink("yandex")) }, ghost = true, enabled = enabled, tag = "Account.UnlinkYandex")
            }
        }
    }
    if (!state.confirmDelete) {
        ZButton(stringResource(R.string.account_delete), { onEvent(AccountEvent.RequestDelete) }, ghost = true, enabled = enabled, tag = "Account.Delete")
    } else {
        Text(stringResource(R.string.account_delete_confirm), style = Zapara.typography.body, color = Zapara.colors.text1)
        Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.account_delete), { onEvent(AccountEvent.ConfirmDelete) }, enabled = enabled, tag = "Account.ConfirmDelete")
            ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelDelete) }, ghost = true, tag = "Account.CancelDelete")
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
