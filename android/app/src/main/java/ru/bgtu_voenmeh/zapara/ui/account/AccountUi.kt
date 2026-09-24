package ru.bgtu_voenmeh.zapara.ui.account

import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
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
    val documentsAccepted: Boolean = false,
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
    val hasPassword: Boolean? = null,
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
    val showPasswordChange get() = showAccount && hasPassword == true
    val showPasswordProof get() = showAccount && hasPassword == true
    val showExport get() = showAccount
    val showDelete get() = showAccount
    val showIdentities get() = showAccount && (vkAvailable || yandexAvailable || identities.isNotEmpty())
    val showVkLink get() = showAccount && vkAvailable && identities.none { it.provider == "vk" }
    val showYandexLink get() = showAccount && yandexAvailable && identities.none { it.provider == "yandex" }
    val showVkUnlink get() = showAccount && identities.any { it.provider == "vk" } && (hasPassword == true || identities.size > 1)
    val showYandexUnlink get() = showAccount && identities.any { it.provider == "yandex" } && (hasPassword == true || identities.size > 1)

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
            if (!registrationAvailable) this else copy(registration = !registration, password = "", documentsAccepted = false)
        is AccountEvent.AcceptDocuments -> copy(documentsAccepted = event.value)
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
    data class AcceptDocuments(val value: Boolean) : AccountEvent
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
fun AccountCard(state: AccountUiState, onEvent: (AccountEvent) -> Unit, onOpenLegal: (String) -> Unit = {}) {
    val c = Zapara.colors
    ZCard(Modifier.fillMaxWidth().testTag("Account.Card")) {
        Text(stringResource(R.string.account_title), style = Zapara.typography.section, color = c.text1)
        Text(state.status, style = Zapara.typography.body, color = c.text1, modifier = Modifier.testTag("Account.Status"))
        Text(stringResource(R.string.account_isolation), style = Zapara.typography.caption, color = c.text2)
        LegalLink("Пользовательское соглашение", "Legal.Agreement", R.drawable.ic_file) { onOpenLegal("agreement") }
        LegalLink("Политика обработки персональных данных", "Legal.Policy", R.drawable.ic_shield) { onOpenLegal("policy") }
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
                AcceptDocuments(
                    checked = state.documentsAccepted,
                    onChange = { onEvent(AccountEvent.AcceptDocuments(it)) }
                )
            }
            Text(stringResource(R.string.account_validation), style = Zapara.typography.caption, color = c.text2)
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.registration) {
                    ZButton(stringResource(R.string.account_register), { onEvent(AccountEvent.Submit) }, enabled = !state.busy && state.documentsAccepted, tag = "Account.Register")
                } else {
                    ZButton(stringResource(R.string.account_login), { onEvent(AccountEvent.Submit) }, enabled = !state.busy, tag = "Account.Login")
                }
                if (state.registrationAvailable) {
                    ZButton(stringResource(R.string.account_mode), { onEvent(AccountEvent.ToggleRegistration) }, ghost = true, enabled = !state.busy, tag = "Account.Mode")
                }
            }
            if (state.showYandexLogin || state.showVkLogin) {
                Text("Войти с помощью", style = Zapara.typography.caption, color = c.text2)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    if (state.showYandexLogin) {
                        IdButton("Яндекс ID", stringResource(R.string.account_yandex), R.drawable.ic_brand_yandex, { onEvent(AccountEvent.StartYandex) }, !state.busy, "Account.Yandex", Modifier.weight(1f))
                    }
                    if (state.showVkLogin) {
                        IdButton("VK ID", stringResource(R.string.account_vk), R.drawable.ic_brand_vk, { onEvent(AccountEvent.StartVk) }, !state.busy, "Account.Vk", Modifier.weight(1f))
                    }
                }
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
    if (state.showPasswordChange) {
        AccountField(state.currentPassword, stringResource(R.string.account_current_password), "Account.CurrentPassword", password = true) {
            onEvent(AccountEvent.CurrentPassword(it))
        }
        AccountField(state.newPassword, stringResource(R.string.account_new_password), "Account.NewPassword", password = true) {
            onEvent(AccountEvent.NewPassword(it))
        }
        ZButton(stringResource(R.string.account_change_password), { onEvent(AccountEvent.ChangePassword) }, enabled = enabled, tag = "Account.ChangePassword")
    }
    if (state.showPasswordProof) {
        AccountField(state.proof, stringResource(R.string.account_proof), "Account.Proof", password = true) {
            onEvent(AccountEvent.Proof(it))
        }
    } else if (state.identities.isNotEmpty()) {
        Text(stringResource(R.string.account_proof_provider), style = Zapara.typography.caption, color = Zapara.colors.text2)
    }
    ZButton(stringResource(R.string.account_export), { onEvent(AccountEvent.CreateExport) }, ghost = true, enabled = enabled, tag = "Account.Export")
    if (state.exportReady) {
        ZButton(stringResource(R.string.account_export_download), { onEvent(AccountEvent.DownloadExport) }, enabled = enabled, tag = "Account.ExportDownload")
    }
    if (state.showIdentities) {
        Text(stringResource(R.string.account_identities), style = Zapara.typography.section, color = Zapara.colors.text1)
        if (state.showYandexLink || state.showVkLink) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.showYandexLink) {
                    IdButton(stringResource(R.string.account_link), "Привязать Яндекс ID", R.drawable.ic_brand_yandex, { onEvent(AccountEvent.LinkYandex) }, enabled, "Account.LinkYandex", Modifier.weight(1f))
                }
                if (state.showVkLink) {
                    IdButton(stringResource(R.string.account_link), "Привязать VK ID", R.drawable.ic_brand_vk, { onEvent(AccountEvent.LinkVk) }, enabled, "Account.LinkVk", Modifier.weight(1f))
                }
            }
        }
        if (state.showYandexUnlink || state.showVkUnlink) {
            if (state.showYandexUnlink) {
                ZButton(stringResource(R.string.account_unlink) + " · Яндекс ID", { onEvent(AccountEvent.Unlink("yandex")) }, ghost = true, enabled = enabled, tag = "Account.UnlinkYandex")
            }
            if (state.showVkUnlink) {
                ZButton(stringResource(R.string.account_unlink) + " · VK ID", { onEvent(AccountEvent.Unlink("vk")) }, ghost = true, enabled = enabled, tag = "Account.UnlinkVk")
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
private fun LegalLink(title: String, tag: String, @DrawableRes icon: Int, onClick: () -> Unit) {
    val c = Zapara.colors
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp).testTag(tag).clickable(onClick = onClick),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)
    ) {
        Icon(painterResource(icon), null, Modifier.size(18.dp), tint = c.text2)
        Text(title, style = Zapara.typography.body, color = c.text1)
    }
}

@Composable
private fun AcceptDocuments(checked: Boolean, onChange: (Boolean) -> Unit) {
    val c = Zapara.colors
    val sentence = stringResource(R.string.account_accept)
    val shape = RoundedCornerShape(Zapara.radii.chip)
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp)
            .testTag("Account.AcceptDocuments")
            .semantics { contentDescription = sentence }
            .toggleable(value = checked, role = Role.Checkbox, onValueChange = onChange),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(
            Modifier.size(22.dp).clip(shape).background(if (checked) c.accent else Color.Transparent).border(1.dp, if (checked) c.accent else c.lineStrong, shape),
            contentAlignment = Alignment.Center
        ) {
            if (checked) Icon(painterResource(R.drawable.ic_check), null, Modifier.size(14.dp), tint = c.onAccent)
        }
        Spacer(Modifier.width(12.dp))
        Icon(painterResource(R.drawable.ic_file), null, Modifier.size(16.dp), tint = c.text2)
        Spacer(Modifier.width(4.dp))
        Icon(painterResource(R.drawable.ic_shield), null, Modifier.size(16.dp), tint = c.text2)
        Spacer(Modifier.width(8.dp))
        Text(sentence, style = Zapara.typography.body, color = c.text1, modifier = Modifier.weight(1f))
    }
}

@Composable
private fun IdButton(
    label: String,
    description: String,
    @DrawableRes mark: Int,
    onClick: () -> Unit,
    enabled: Boolean,
    tag: String,
    modifier: Modifier = Modifier
) {
    val shape = RoundedCornerShape(Zapara.radii.control)
    Surface(
        modifier
            .testTag(tag)
            .semantics { contentDescription = description }
            .heightIn(min = 48.dp)
            .alpha(if (enabled) 1f else 0.45f)
            .clip(shape)
            .clickable(enabled = enabled, role = Role.Button, onClick = onClick),
        shape = shape,
        color = Color.White,
        contentColor = Color(0xFF111111),
        border = BorderStroke(1.dp, Color(0xFFE6E6E6))
    ) {
        Row(
            Modifier.padding(horizontal = 12.dp).fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center
        ) {
            Image(painterResource(mark), contentDescription = null, modifier = Modifier.size(24.dp))
            Spacer(Modifier.width(8.dp))
            Text(label, style = Zapara.typography.bodyStrong, color = Color(0xFF111111), maxLines = 1, overflow = TextOverflow.Ellipsis)
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
