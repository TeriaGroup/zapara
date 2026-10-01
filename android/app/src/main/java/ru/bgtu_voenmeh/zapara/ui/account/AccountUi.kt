package ru.bgtu_voenmeh.zapara.ui.account

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.repeatOnLifecycle
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
import ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation
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
) {
    val label: String get() = "$deviceName · ${when (platform.lowercase()) {
        "android" -> "Android"
        "windows" -> "Windows"
        "web" -> "Веб"
        else -> platform.ifBlank { "Устройство" }
    }} · …${deviceId.takeLast(6).uppercase()}"
}

internal fun mergeAccountDevices(current: List<AccountDeviceRow>, incoming: List<AccountDeviceRow>): List<AccountDeviceRow> =
    (current + incoming).associateBy { it.familyId }.values.toList()

data class AccountIdentityRow(val provider: String)

enum class AccountRecoveryStep { Request, Confirm }

data class AccountUiCapabilities(
    val registration: Boolean = false,
    val vk: Boolean = false,
    val yandex: Boolean = false,
    val recovery: Boolean = false
)

data class AccountUiState(
    val ready: Boolean = false,
    val busy: Boolean = false,
    val externalPending: Boolean = false,
    val pendingExternalProvider: String? = null,
    val configured: Boolean = false,
    val guest: Boolean = true,
    val registration: Boolean = false,
    val registrationAvailable: Boolean = false,
    val documentsAccepted: Boolean = false,
    val username: String = "",
    val password: String = "",
    val displayName: String = "",
    val profileNameBaseline: String = "",
    val profileError: String? = null,
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
    val exportPending: Boolean = false,
    val exportSaveName: String? = null,
    val exportSaveVersion: Long = 0,
    val exportSaveToken: String? = null,
    val confirmDelete: Boolean = false,
    val vkAvailable: Boolean = false,
    val yandexAvailable: Boolean = false,
    val recoveryAvailable: Boolean = false,
    val capabilitiesError: Boolean = false,
    val capabilitiesLoading: Boolean = false,
    val deviceCursor: String? = null,
    val confirmRevoke: String? = null,
    val recoveryStep: AccountRecoveryStep = AccountRecoveryStep.Request
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
    val usernameValid get() = runCatching { AccountValidation.username(username) }.isSuccess
    val passwordValid get() = runCatching { AccountValidation.password(password) }.isSuccess
    val registrationNameValid get() = displayName.isBlank() || runCatching { AccountValidation.displayName(displayName) }.isSuccess
    val canSubmitCredentials get() = showGuestAuth && !busy && !externalPending && usernameValid && passwordValid &&
        (!registration || registrationAvailable && documentsAccepted && registrationNameValid)
    val canRequestRecovery get() = recoveryUsername.ifBlank { username }.trim().matches(Regex("[A-Za-z0-9_.-]{3,32}"))
    val canConfirmRecovery get() = proof.trim().isNotEmpty() && newPassword.length in 12..128
    val canSaveProfile get() = showAccount && !busy && displayName.trim() != profileNameBaseline.trim() &&
        runCatching { displayName.trim().takeIf { it.isNotEmpty() }?.let(AccountValidation::displayName) }.isSuccess

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
        is AccountEvent.ProfileName -> copy(displayName = event.value, profileError = null)
        AccountEvent.CancelProfile -> copy(displayName = profileNameBaseline, profileError = null)
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
        is AccountEvent.RequestRevoke -> if (devices.any { it.familyId == event.familyId })
            copy(confirmRevoke = event.familyId) else this
        AccountEvent.RequestRevokeAll -> if (devices.isNotEmpty()) copy(confirmRevoke = "all") else this
        AccountEvent.CancelRevoke -> copy(confirmRevoke = null)
        AccountEvent.ClearSensitive -> clearSecrets()
        AccountEvent.BackToRecoveryRequest -> copy(recoveryStep = AccountRecoveryStep.Request).clearSecrets()
        else -> this
    }
}

sealed interface AccountEvent {
    data object RefreshProfile : AccountEvent
    data object RetryCapabilities : AccountEvent
    data object CancelExternal : AccountEvent
    data object CheckExport : AccountEvent
    data class SaveExport(val uri: android.net.Uri?, val version: Long, val token: String) : AccountEvent
    data object RetryExportSave : AccountEvent
    data class Username(val value: String) : AccountEvent
    data class Password(val value: String) : AccountEvent
    data class DisplayName(val value: String) : AccountEvent
    data class ProfileName(val value: String) : AccountEvent
    data object SaveProfile : AccountEvent
    data object CancelProfile : AccountEvent
    data object ClearSensitive : AccountEvent
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
    data object LoadMoreDevices : AccountEvent
    data class RequestRevoke(val familyId: String) : AccountEvent
    data object RequestRevokeAll : AccountEvent
    data object ConfirmRevoke : AccountEvent
    data object CancelRevoke : AccountEvent
    data class Revoke(val familyId: String) : AccountEvent
    data object ChangePassword : AccountEvent
    data object CreateExport : AccountEvent
    data object DownloadExport : AccountEvent
    data object RequestDelete : AccountEvent
    data object ConfirmDelete : AccountEvent
    data object CancelDelete : AccountEvent
    data object RequestReset : AccountEvent
    data object ConfirmReset : AccountEvent
    data object BackToRecoveryRequest : AccountEvent
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
    val lifecycle = androidx.compose.ui.platform.LocalLifecycleOwner.current.lifecycle
    val currentEvent = androidx.compose.runtime.rememberUpdatedState(onEvent)
    androidx.compose.runtime.LaunchedEffect(lifecycle, state.showAccount) {
        if (state.showAccount) lifecycle.repeatOnLifecycle(androidx.lifecycle.Lifecycle.State.STARTED) {
            while (true) { currentEvent.value(AccountEvent.RefreshProfile); kotlinx.coroutines.delay(60_000) }
        }
    }
    ZCard(Modifier.fillMaxWidth().testTag("Account.Card")) {
        Text(stringResource(R.string.account_title), style = Zapara.typography.section, color = c.text1)
        Text(state.status, style = Zapara.typography.body, color = c.text1, modifier = Modifier.testTag("Account.Status"))
        if (state.externalPending) {
            Text(stringResource(R.string.ux60_account_external_waiting,
                state.pendingExternalProvider?.uppercase() ?: stringResource(R.string.account_title)),
                style = Zapara.typography.caption, color = c.text2)
            ZButton(stringResource(R.string.ux60_account_external_cancel),
                { onEvent(AccountEvent.CancelExternal) }, ghost = true,
                enabled = !state.busy, tag = "Account.CancelExternal")
        }
        if (state.capabilitiesLoading) Text(stringResource(R.string.ux30_account_caps_loading),
            style = Zapara.typography.caption, color = c.text2)
        if (state.capabilitiesError) {
            Text(stringResource(R.string.ux30_account_caps_failed), style = Zapara.typography.body, color = c.text2,
                modifier = Modifier.testTag("Account.CapabilitiesError"))
            ZButton(stringResource(R.string.repeat), { onEvent(AccountEvent.RetryCapabilities) },
                ghost = true, enabled = !state.capabilitiesLoading, tag = "Account.RetryCapabilities")
        }
        Text(stringResource(R.string.account_isolation), style = Zapara.typography.caption, color = c.text2)
        LegalLink(stringResource(R.string.face_agreement), "Legal.Agreement", R.drawable.ic_file) { onOpenLegal("agreement") }
        LegalLink(stringResource(R.string.face_policy), "Legal.Policy", R.drawable.ic_shield) { onOpenLegal("policy") }
        if (!state.configured || !state.ready) return@ZCard
        if (state.guest) {
            AccountField(state.username, stringResource(R.string.account_username), "Account.Username") {
                onEvent(AccountEvent.Username(it))
            }
            if (!state.usernameValid) Text(stringResource(R.string.ux60_account_username_hint),
                style = Zapara.typography.caption, color = c.warn)
            AccountField(state.password, stringResource(R.string.account_password), "Account.Password", password = true) {
                onEvent(AccountEvent.Password(it))
            }
            if (!state.passwordValid) Text(stringResource(R.string.ux60_account_password_hint),
                style = Zapara.typography.caption, color = c.warn)
            if (state.registration) {
                AccountField(state.displayName, stringResource(R.string.account_display_name), "Account.DisplayName") {
                    onEvent(AccountEvent.DisplayName(it))
                }
                if (!state.registrationNameValid) Text(stringResource(R.string.ux60_account_name_hint),
                    style = Zapara.typography.caption, color = c.warn)
                AcceptDocuments(
                    checked = state.documentsAccepted,
                    onChange = { onEvent(AccountEvent.AcceptDocuments(it)) }
                )
            }
            Text(stringResource(R.string.account_validation), style = Zapara.typography.caption, color = c.text2)
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.registration) {
                    ZButton(stringResource(R.string.account_register), { onEvent(AccountEvent.Submit) }, enabled = state.canSubmitCredentials, tag = "Account.Register")
                } else {
                    ZButton(stringResource(R.string.account_login), { onEvent(AccountEvent.Submit) }, enabled = state.canSubmitCredentials, tag = "Account.Login")
                }
                if (state.registrationAvailable) {
                    ZButton(stringResource(R.string.account_mode), { onEvent(AccountEvent.ToggleRegistration) }, ghost = true, enabled = !state.busy && !state.externalPending, tag = "Account.Mode")
                }
            }
            if (state.showYandexLogin || state.showVkLogin) {
                Text(stringResource(R.string.face_sign_in_with), style = Zapara.typography.caption, color = c.text2)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    if (state.showYandexLogin) {
                        IdButton(stringResource(R.string.face_yandex_id), stringResource(R.string.account_yandex), R.drawable.ic_brand_yandex, { onEvent(AccountEvent.StartYandex) }, !state.busy && !state.externalPending, "Account.Yandex", Modifier.weight(1f))
                    }
                    if (state.showVkLogin) {
                        IdButton("VK ID", stringResource(R.string.account_vk), R.drawable.ic_brand_vk, { onEvent(AccountEvent.StartVk) }, !state.busy && !state.externalPending, "Account.Vk", Modifier.weight(1f))
                    }
                }
            }
            if (state.showRecovery) {
                AccountField(state.recoveryUsername, stringResource(R.string.ux30_recovery_login), "Account.Recovery") {
                    onEvent(AccountEvent.RecoveryUsername(it))
                }
                if (state.recoveryStep == AccountRecoveryStep.Request) {
                    ZButton(stringResource(R.string.ux30_recovery_request), { onEvent(AccountEvent.RequestReset) },
                        enabled = !state.busy && state.canRequestRecovery, tag = "Account.Reset")
                } else {
                    Text(stringResource(R.string.ux30_recovery_requested), style = Zapara.typography.caption, color = c.text2)
                    AccountField(state.proof, stringResource(R.string.account_proof), "Account.Proof", password = true) {
                        onEvent(AccountEvent.Proof(it))
                    }
                    AccountField(state.newPassword, stringResource(R.string.account_new_password), "Account.NewPassword", password = true) {
                        onEvent(AccountEvent.NewPassword(it))
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        ZButton(stringResource(R.string.ux30_recovery_set), { onEvent(AccountEvent.ConfirmReset) },
                            enabled = !state.busy && state.canConfirmRecovery, tag = "Account.ConfirmReset")
                        ZButton(stringResource(R.string.ux30_recovery_back), { onEvent(AccountEvent.BackToRecoveryRequest) },
                            ghost = true, enabled = !state.busy)
                    }
                }
            }
        } else {
            Text(state.accountName, style = Zapara.typography.section, color = c.text1, modifier = Modifier.testTag("Account.Name"))
            AccountField(state.displayName, stringResource(R.string.uxnext_profile_name), "Account.ProfileName") {
                onEvent(AccountEvent.ProfileName(it))
            }
            state.profileError?.let { Text(it, style = Zapara.typography.caption, color = c.bad,
                modifier = Modifier.testTag("Account.ProfileError")) }
            if (state.displayName.trim() != state.profileNameBaseline.trim()) {
                Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.uxnext_profile_save), { onEvent(AccountEvent.SaveProfile) },
                        enabled = state.canSaveProfile, busy = state.busy, tag = "Account.ProfileSave")
                    ZButton(stringResource(R.string.uxnext_profile_cancel), { onEvent(AccountEvent.CancelProfile) },
                        ghost = true, enabled = !state.busy, tag = "Account.ProfileCancel")
                }
            }
            ru.bgtu_voenmeh.zapara.ui.chat.LocalAvatarStore.current?.let { avatars ->
                ru.bgtu_voenmeh.zapara.ui.chat.AvatarEditor(state.accountName,
                    ru.bgtu_voenmeh.zapara.data.avatars.AvatarTarget(ru.bgtu_voenmeh.zapara.data.avatars.AvatarKind.User, avatars.userId),
                    enabled = !state.busy)
            }
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
    val enabled = !state.busy && !state.externalPending
    var launchedExportVersion by rememberSaveable { mutableLongStateOf(0L) }
    var launchedExportToken by rememberSaveable { mutableStateOf("") }
    var launchedExportKey by rememberSaveable { mutableStateOf("") }
    val createExportDocument = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/json")) {
        onEvent(AccountEvent.SaveExport(it, launchedExportVersion, launchedExportToken))
    }
    LaunchedEffect(state.exportSaveVersion, state.exportSaveName, state.exportSaveToken) {
        val name = state.exportSaveName ?: return@LaunchedEffect
        val token = state.exportSaveToken ?: return@LaunchedEffect
        val key = "$token:${state.exportSaveVersion}"
        if (key == launchedExportKey) return@LaunchedEffect
        launchedExportKey = key
        launchedExportVersion = state.exportSaveVersion
        launchedExportToken = token
        createExportDocument.launch(name)
    }
    ZButton(stringResource(R.string.account_devices), { onEvent(AccountEvent.LoadDevices) }, ghost = true, enabled = enabled, tag = "Account.Devices")
    state.devices.forEach { device ->
        Text(device.label, style = Zapara.typography.body, color = Zapara.colors.text1, modifier = Modifier.testTag("Account.Device"))
        if (device.current) Text(stringResource(R.string.ux30_devices_current),
            style = Zapara.typography.caption, color = Zapara.colors.text2)
        if (state.confirmRevoke == device.familyId) {
            Text(if (device.current) stringResource(R.string.ux30_devices_current_confirm)
                else stringResource(R.string.ux30_devices_other_confirm, device.label),
                style = Zapara.typography.body, color = Zapara.colors.text1)
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.account_revoke), { onEvent(AccountEvent.ConfirmRevoke) },
                    enabled = enabled, tag = "Account.ConfirmRevoke")
                ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelRevoke) },
                    ghost = true, enabled = enabled)
            }
        } else {
            ZButton(stringResource(R.string.account_revoke), { onEvent(AccountEvent.RequestRevoke(device.familyId)) },
                ghost = true, enabled = enabled, tag = "Account.Revoke")
        }
    }
    if (state.deviceCursor != null) ZButton(stringResource(R.string.ux30_devices_more),
        { onEvent(AccountEvent.LoadMoreDevices) }, ghost = true, enabled = enabled, tag = "Account.MoreDevices")
    if (state.devices.isNotEmpty()) {
        if (state.confirmRevoke == "all") {
            Text(stringResource(R.string.ux30_devices_all_confirm), style = Zapara.typography.body, color = Zapara.colors.text1)
            Row(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.ux30_devices_all), { onEvent(AccountEvent.ConfirmRevoke) },
                    enabled = enabled, tag = "Account.ConfirmRevokeAll")
                ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelRevoke) },
                    ghost = true, enabled = enabled)
            }
        } else ZButton(stringResource(R.string.ux30_devices_all),
            { onEvent(AccountEvent.RequestRevokeAll) }, ghost = true, enabled = enabled, tag = "Account.RevokeAll")
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
    ZButton(stringResource(R.string.account_export), { onEvent(AccountEvent.CreateExport) }, ghost = true,
        enabled = enabled && !state.exportPending, tag = "Account.Export")
    if (state.exportPending) ZButton(stringResource(R.string.ux60_export_check),
        { onEvent(AccountEvent.CheckExport) }, enabled = enabled, tag = "Account.ExportCheck")
    if (state.exportReady) {
        ZButton(stringResource(R.string.account_export_download), { onEvent(AccountEvent.DownloadExport) },
            enabled = enabled && state.exportSaveName == null, tag = "Account.ExportDownload")
    }
    if (state.exportSaveName != null) ZButton(stringResource(R.string.ux60_export_choose_again),
        { onEvent(AccountEvent.RetryExportSave) }, ghost = true, enabled = enabled,
        tag = "Account.ExportSaveRetry")
    if (state.showIdentities) {
        Text(stringResource(R.string.account_identities), style = Zapara.typography.section, color = Zapara.colors.text1)
        if (state.showYandexLink || state.showVkLink) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.showYandexLink) {
                    IdButton(stringResource(R.string.account_link), stringResource(R.string.face_link_yandex), R.drawable.ic_brand_yandex, { onEvent(AccountEvent.LinkYandex) }, enabled, "Account.LinkYandex", Modifier.weight(1f))
                }
                if (state.showVkLink) {
                    IdButton(stringResource(R.string.account_link), stringResource(R.string.face_link_vk), R.drawable.ic_brand_vk, { onEvent(AccountEvent.LinkVk) }, enabled, "Account.LinkVk", Modifier.weight(1f))
                }
            }
        }
        if (state.showYandexUnlink || state.showVkUnlink) {
            if (state.showYandexUnlink) {
                ZButton(stringResource(R.string.face_unlink_yandex, stringResource(R.string.account_unlink)), { onEvent(AccountEvent.Unlink("yandex")) }, ghost = true, enabled = enabled, tag = "Account.UnlinkYandex")
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
        contentColor = Zapara.colors.idInk,
        border = BorderStroke(1.dp, Zapara.colors.idLine)
    ) {
        Row(
            Modifier.padding(horizontal = 12.dp).fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.Center
        ) {
            Image(painterResource(mark), contentDescription = null, modifier = Modifier.size(24.dp))
            Spacer(Modifier.width(8.dp))
            Text(label, style = Zapara.typography.bodyStrong, color = Zapara.colors.idInk, maxLines = 1, overflow = TextOverflow.Ellipsis)
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
