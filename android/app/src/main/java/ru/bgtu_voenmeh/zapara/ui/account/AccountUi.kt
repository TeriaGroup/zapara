package ru.bgtu_voenmeh.zapara.ui.account

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.annotation.DrawableRes
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.LocalIndication
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.sizeIn
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsFocusedAsState
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.repeatOnLifecycle
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.text.KeyboardActions
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
import ru.bgtu_voenmeh.zapara.ui.theme.ZIconButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZActionButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZDisclosureButton
import ru.bgtu_voenmeh.zapara.ui.theme.ZCard
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import ru.bgtu_voenmeh.zapara.ui.theme.controlFocusRing
import ru.bgtu_voenmeh.zapara.ui.components.pressScale
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64

data class AccountDeviceRow(
    val familyId: String,
    val deviceId: String,
    val deviceName: String,
    val platform: String,
    val current: Boolean,
    val createdAt: java.time.Instant? = null,
    val lastSeenAt: java.time.Instant? = null,
    val expiresAt: java.time.Instant? = null
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
    val devicesLoaded: Boolean = false,
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
    val recoveryStep: AccountRecoveryStep = AccountRecoveryStep.Request,
    val recoveryCompletionVersion: Int = 0
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
    val canConfirmRecovery get() = proof.trim().isNotEmpty() &&
        runCatching { AccountValidation.password(newPassword) }.isSuccess
    val profileNameValid get() = runCatching {
        displayName.trim().takeIf { it.isNotEmpty() }?.let(AccountValidation::displayName)
    }.isSuccess
    val canSaveProfile get() = showAccount && !busy && displayName.trim() != profileNameBaseline.trim() && profileNameValid
    val canChangePassword get() = showPasswordChange && !busy &&
        runCatching { AccountValidation.password(currentPassword); AccountValidation.password(newPassword) }.isSuccess &&
        currentPassword != newPassword
    val canPerformProtectedAction get() = !showPasswordProof ||
        runCatching { AccountValidation.password(proof) }.isSuccess

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

@OptIn(ExperimentalLayoutApi::class)
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
        if (!state.showAccount) Text(stringResource(R.string.account_title), style = Zapara.typography.section, color = c.text1)
        if (state.status.isNotBlank() && (!state.showAccount || state.status != stringResource(R.string.account_local)))
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
        if (!state.showAccount) {
        Text(stringResource(R.string.account_isolation), style = Zapara.typography.caption, color = c.text2)
        ZActionButton(stringResource(R.string.face_agreement), { onOpenLegal("agreement") },
            tag = "Legal.Agreement", leadingIcon = R.drawable.ic_file)
        ZActionButton(stringResource(R.string.face_policy), { onOpenLegal("policy") },
            tag = "Legal.Policy", leadingIcon = R.drawable.ic_shield)
        }
        if (!state.configured || !state.ready) return@ZCard
        if (state.guest) {
            var recoveryOpen by rememberSaveable { mutableStateOf(false) }
            androidx.compose.runtime.LaunchedEffect(state.recoveryCompletionVersion) {
                if (state.recoveryCompletionVersion > 0) recoveryOpen = false
            }
            var confirmPassword by remember(state.registration) { mutableStateOf("") }
            LaunchedEffect(state.password) { if (state.password.isEmpty()) confirmPassword = "" }
            AccountField(state.username, stringResource(R.string.account_username), "Account.Username") {
                onEvent(AccountEvent.Username(it))
            }
            if (!state.usernameValid) Text(stringResource(R.string.ux60_account_username_hint),
                style = Zapara.typography.caption, color = if (state.username.isBlank()) c.text2 else c.text1)
            AccountField(state.password, stringResource(R.string.account_password), "Account.Password", password = true,
                onDone = if (!state.registration && state.canSubmitCredentials) {{ onEvent(AccountEvent.Submit) }} else null) {
                onEvent(AccountEvent.Password(it))
            }
            if (state.registration) PasswordProgress(state.password)
            else if (!state.passwordValid) Text(stringResource(R.string.ux60_account_password_hint),
                style = Zapara.typography.caption, color = if (state.password.isEmpty()) c.text2 else c.text1)
            if (state.registration) {
                AccountField(confirmPassword, stringResource(R.string.ux300_android_confirm_password),
                    "Account.ConfirmPassword", password = true) { confirmPassword = it }
                if (confirmPassword.isNotEmpty() && confirmPassword != state.password)
                    Text(stringResource(R.string.ux300_android_password_mismatch),
                        style = Zapara.typography.caption, color = c.bad,
                        modifier = Modifier.testTag("Account.PasswordMismatch"))
                AccountField(state.displayName, stringResource(R.string.account_display_name), "Account.DisplayName",
                    onDone = if (state.canSubmitCredentials && confirmPassword == state.password) {{ onEvent(AccountEvent.Submit) }} else null) {
                    onEvent(AccountEvent.DisplayName(it))
                }
                if (!state.registrationNameValid) Text(stringResource(R.string.ux60_account_name_hint),
                    style = Zapara.typography.caption, color = if (state.displayName.isBlank()) c.text2 else c.text1)
                AcceptDocuments(
                    checked = state.documentsAccepted,
                    onChange = { onEvent(AccountEvent.AcceptDocuments(it)) }
                )
            }
            FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                if (state.registration) {
                    ZButton(stringResource(R.string.account_register), { onEvent(AccountEvent.Submit) },
                        enabled = state.canSubmitCredentials && confirmPassword == state.password,
                        tag = "Account.Register")
                } else {
                    ZButton(stringResource(R.string.account_login), { onEvent(AccountEvent.Submit) }, enabled = state.canSubmitCredentials, tag = "Account.Login")
                }
                if (state.registrationAvailable) {
                    ZButton(stringResource(R.string.account_mode), { onEvent(AccountEvent.ToggleRegistration) }, ghost = true, enabled = !state.busy && !state.externalPending, tag = "Account.Mode")
                }
            }
            if (state.showYandexLogin || state.showVkLogin) {
                Text(stringResource(R.string.face_sign_in_with), style = Zapara.typography.caption, color = c.text2)
                val largeText = androidx.compose.ui.platform.LocalDensity.current.fontScale >= 1.5f
                if (largeText) Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    if (state.showYandexLogin) {
                        IdButton(stringResource(R.string.face_yandex_id), stringResource(R.string.account_yandex), R.drawable.ic_brand_yandex,
                            { onEvent(AccountEvent.StartYandex) }, !state.busy && !state.externalPending,
                            "Account.Yandex", Modifier.fillMaxWidth())
                    }
                    if (state.showVkLogin) {
                        IdButton("VK ID", stringResource(R.string.account_vk), R.drawable.ic_brand_vk,
                            { onEvent(AccountEvent.StartVk) }, !state.busy && !state.externalPending,
                            "Account.Vk", Modifier.fillMaxWidth())
                    }
                } else Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    if (state.showYandexLogin) {
                        IdButton(stringResource(R.string.face_yandex_id), stringResource(R.string.account_yandex), R.drawable.ic_brand_yandex, { onEvent(AccountEvent.StartYandex) }, !state.busy && !state.externalPending, "Account.Yandex", Modifier.weight(1f))
                    }
                    if (state.showVkLogin) {
                        IdButton("VK ID", stringResource(R.string.account_vk), R.drawable.ic_brand_vk, { onEvent(AccountEvent.StartVk) }, !state.busy && !state.externalPending, "Account.Vk", Modifier.weight(1f))
                    }
                }
            }
            if (state.showRecovery && state.recoveryStep == AccountRecoveryStep.Request) {
                ZButton(stringResource(if (recoveryOpen)
                    R.string.ux30_platform_recovery_hide else R.string.ux30_platform_recovery_open),
                    { recoveryOpen = !recoveryOpen }, ghost = true, tag = "Account.RecoveryToggle")
            }
            if (state.showRecovery && (recoveryOpen || state.recoveryStep == AccountRecoveryStep.Confirm)) {
                if (state.recoveryStep == AccountRecoveryStep.Request && state.recoveryUsername.isBlank() && state.username.isNotBlank())
                    ZButton(stringResource(R.string.ux100_platform_use_login),
                        { onEvent(AccountEvent.RecoveryUsername(state.username)) }, ghost = true,
                        tag = "Account.UseLoginForRecovery")
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
                    AccountField(state.newPassword, stringResource(R.string.account_new_password), "Account.NewPassword", password = true,
                        onDone = if (!state.busy && state.canConfirmRecovery) {{ onEvent(AccountEvent.ConfirmReset) }} else null) {
                        onEvent(AccountEvent.NewPassword(it))
                    }
                    FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                        verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        ZButton(stringResource(R.string.ux30_recovery_set), { onEvent(AccountEvent.ConfirmReset) },
                            enabled = !state.busy && state.canConfirmRecovery, tag = "Account.ConfirmReset")
                        ZButton(stringResource(R.string.ux30_recovery_back), { onEvent(AccountEvent.BackToRecoveryRequest) },
                            ghost = true, enabled = !state.busy)
                    }
                }
            }
        } else {
            var editName by rememberSaveable(state.accountName) { mutableStateOf(false) }
            val nameDirty = state.displayName.trim() != state.profileNameBaseline.trim()
            val avatars = ru.bgtu_voenmeh.zapara.ui.chat.LocalAvatarStore.current
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(Zapara.space.m)) {
                if (avatars != null) ru.bgtu_voenmeh.zapara.ui.chat.AvatarEditor(state.accountName,
                    ru.bgtu_voenmeh.zapara.data.avatars.AvatarTarget(ru.bgtu_voenmeh.zapara.data.avatars.AvatarKind.User, avatars.userId),
                    enabled = !state.busy, compact = true)
                else ru.bgtu_voenmeh.zapara.ui.chat.ChatAvatar(state.accountName, null, 56.dp)
                Text(state.accountName, style = Zapara.typography.section, color = c.text1,
                    modifier = Modifier.weight(1f).testTag("Account.Name"))
                ZIconButton(R.drawable.ic_pencil, stringResource(R.string.account_profile_edit),
                    { editName = true }, "Account.ProfileEdit", enabled = !state.busy && !state.externalPending)
            }
            if (editName || nameDirty || state.profileError != null) {
            AccountField(state.displayName, stringResource(R.string.uxnext_profile_name), "Account.ProfileName") {
                onEvent(AccountEvent.ProfileName(it))
            }
            if (state.displayName.isNotBlank() && !state.profileNameValid &&
                state.displayName.trim() != state.profileNameBaseline.trim())
                Text(stringResource(R.string.ux60_account_name_hint), style = Zapara.typography.caption, color = c.bad)
            state.profileError?.let { Text(it, style = Zapara.typography.caption, color = c.bad,
                modifier = Modifier.testTag("Account.ProfileError")) }
                FlowRow(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s),
                    verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.theme_save), { onEvent(AccountEvent.SaveProfile) },
                        enabled = state.canSaveProfile, busy = state.busy, tag = "Account.ProfileSave")
                    ZButton(stringResource(R.string.theme_cancel), { editName = false; onEvent(AccountEvent.CancelProfile) },
                        ghost = true, enabled = !state.busy, tag = "Account.ProfileCancel")
                }
            }
            AccountLifecyclePanel(state, onEvent)
            var documentsOpen by rememberSaveable(state.accountName) { mutableStateOf(false) }
            HorizontalDivider(color = c.line, thickness = Zapara.space.hairline)
            ZDisclosureButton(stringResource(R.string.account_section_documents), documentsOpen,
                { documentsOpen = !documentsOpen }, tag = "Account.Documents", quiet = true)
            if (documentsOpen) {
                Text(stringResource(R.string.account_isolation), style = Zapara.typography.caption, color = c.text2)
                ZActionButton(stringResource(R.string.face_agreement), { onOpenLegal("agreement") },
                    tag = "Legal.Agreement", leadingIcon = R.drawable.ic_file, quiet = true)
                ZActionButton(stringResource(R.string.face_policy), { onOpenLegal("policy") },
                    tag = "Legal.Policy", leadingIcon = R.drawable.ic_shield, quiet = true)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AccountLifecyclePanel(state: AccountUiState, onEvent: (AccountEvent) -> Unit) {
    val enabled = !state.busy && !state.externalPending
    var devicesOpen by rememberSaveable(state.accountName) { mutableStateOf(false) }
    var securityOpen by rememberSaveable(state.accountName) { mutableStateOf(false) }
    var dataOpen by rememberSaveable(state.accountName) { mutableStateOf(false) }
    var deviceFilter by rememberSaveable(state.accountName) { mutableStateOf("all") }
    var devicePlatform by rememberSaveable(state.accountName) { mutableStateOf("") }
    var deviceQuery by rememberSaveable(state.accountName) { mutableStateOf("") }
    val platforms = state.devices.map { it.platform.lowercase() }.filter(String::isNotBlank).distinct().sorted()
    val visibleDevices = state.devices.filter { device ->
        (deviceFilter == "all" || !device.current) &&
            (devicePlatform.isBlank() || device.platform.equals(devicePlatform, ignoreCase = true)) &&
            (deviceQuery.isBlank() || device.label.contains(deviceQuery.trim(), ignoreCase = true))
    }
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
    ZDisclosureButton(stringResource(R.string.account_devices), expanded = devicesOpen, onClick = {
        devicesOpen = !devicesOpen
        if (devicesOpen) onEvent(AccountEvent.LoadDevices)
    }, enabled = enabled, tag = "Account.Devices", quiet = true)
    if (devicesOpen && state.devicesLoaded)
        ZButton(stringResource(R.string.ux30_platform_refresh_devices), { onEvent(AccountEvent.LoadDevices) },
            ghost = true, enabled = enabled, tag = "Account.RefreshDevices")
    if (devicesOpen && state.devicesLoaded && state.devices.isEmpty())
        Text(stringResource(R.string.ux30_platform_no_devices), style = Zapara.typography.caption,
            color = Zapara.colors.text2, modifier = Modifier.testTag("Account.NoDevices"))
    if (devicesOpen) {
    if (state.devices.isNotEmpty()) {
        FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.ux100_platform_all_devices, state.devices.size),
                { deviceFilter = "all" }, ghost = deviceFilter != "all", tag = "Account.DevicesAll")
            ZButton(stringResource(R.string.ux100_platform_other_devices, state.devices.count { !it.current }),
                { deviceFilter = "other" }, ghost = deviceFilter != "other", tag = "Account.DevicesOther")
        }
        if (deviceFilter == "other" && state.devices.none { !it.current })
            Text(stringResource(if (state.deviceCursor == null) R.string.ux100_platform_no_other_devices
                else R.string.ux100_platform_load_other_devices),
                style = Zapara.typography.caption, color = Zapara.colors.text2)
        AccountField(deviceQuery, stringResource(R.string.ux300_android_device_search),
            "Account.DeviceSearch") { deviceQuery = it }
        if (platforms.size > 1) FlowRow(horizontalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            ZChip(stringResource(R.string.ux300_android_all_platforms), selected = devicePlatform.isBlank(),
                onClick = { devicePlatform = "" }, tag = "Account.Platform.All")
            platforms.forEach { platform -> ZChip(platform, selected = devicePlatform == platform,
                onClick = { devicePlatform = platform }, tag = "Account.Platform.$platform") }
        }
        Text(stringResource(R.string.ux300_android_device_search_scope, visibleDevices.size,
            state.devices.size), style = Zapara.typography.caption, color = Zapara.colors.text2)
        if (visibleDevices.isEmpty() && (deviceQuery.isNotBlank() || devicePlatform.isNotBlank()))
            ZButton(stringResource(R.string.ux300_android_device_reset), {
                deviceQuery = ""; devicePlatform = ""
            }, ghost = true, tag = "Account.DeviceReset")
    }
    visibleDevices.forEachIndexed { index, device ->
        Column(Modifier.fillMaxWidth().padding(vertical = Zapara.space.s),
            verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
            Text(device.label, style = Zapara.typography.body, color = Zapara.colors.text1, modifier = Modifier.testTag("Account.Device"))
            if (device.current) Text(stringResource(R.string.ux30_devices_current),
                style = Zapara.typography.caption, color = Zapara.colors.text2)
            var detailOpen by rememberSaveable(device.familyId) { mutableStateOf(false) }
            ZDisclosureButton(stringResource(R.string.ux300_ext_device_details), expanded = detailOpen,
                onClick = { detailOpen = !detailOpen }, tag = "Account.DeviceDetails.${device.familyId}")
            if (detailOpen) {
                val format = java.time.format.DateTimeFormatter.ofPattern("d MMM yyyy, HH:mm", java.util.Locale.forLanguageTag("ru"))
                    .withZone(java.time.ZoneId.systemDefault())
                listOf(R.string.ux300_ext_device_created to device.createdAt,
                    R.string.ux300_ext_device_seen to device.lastSeenAt,
                    R.string.ux300_ext_device_expires to device.expiresAt).forEach { (label, instant) ->
                    if (instant != null) Text(stringResource(label, format.format(instant)), style = Zapara.typography.caption)
                }
                Text(device.deviceId, style = Zapara.typography.caption)
            }
            if (state.confirmRevoke == device.familyId) {
                Text(if (device.current) stringResource(R.string.ux30_devices_current_confirm)
                    else stringResource(R.string.ux30_devices_other_confirm, device.label),
                    style = Zapara.typography.body, color = Zapara.colors.text1)
                Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                    ZButton(stringResource(R.string.account_revoke), { onEvent(AccountEvent.ConfirmRevoke) },
                        modifier = Modifier.fillMaxWidth(), enabled = enabled, tag = "Account.ConfirmRevoke")
                    ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelRevoke) },
                        modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Account.CancelRevoke")
                }
            } else {
                ZButton(stringResource(R.string.account_revoke), { onEvent(AccountEvent.RequestRevoke(device.familyId)) },
                    ghost = true, enabled = enabled, tag = "Account.Revoke")
            }
        }
        if (index < visibleDevices.lastIndex) HorizontalDivider(thickness = Zapara.space.hairline,
            color = Zapara.colors.line)
    }
    if (state.deviceCursor != null) ZButton(stringResource(R.string.ux30_devices_more),
        { onEvent(AccountEvent.LoadMoreDevices) }, ghost = true, enabled = enabled, tag = "Account.MoreDevices")
    if (state.devices.isNotEmpty()) {
        if (state.confirmRevoke == "all") {
            Text(stringResource(R.string.ux30_devices_all_confirm), style = Zapara.typography.body, color = Zapara.colors.text1)
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.ux30_devices_all), { onEvent(AccountEvent.ConfirmRevoke) },
                    modifier = Modifier.fillMaxWidth(), enabled = enabled, tag = "Account.ConfirmRevokeAll")
                ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelRevoke) },
                    modifier = Modifier.fillMaxWidth(), ghost = true, enabled = enabled, tag = "Account.CancelRevoke")
            }
        } else ZButton(stringResource(R.string.ux30_devices_all),
            { onEvent(AccountEvent.RequestRevokeAll) }, ghost = true, enabled = enabled, tag = "Account.RevokeAll")
    }
    }
    val securityExpanded = securityOpen || state.confirmLogout
    ZDisclosureButton(stringResource(R.string.account_section_security), expanded = securityExpanded, onClick = {
        securityOpen = !securityExpanded
        if (state.confirmLogout) onEvent(AccountEvent.CancelLogout)
        if (!securityOpen) onEvent(AccountEvent.ClearSensitive)
    }, tag = "Account.Security", quiet = true)
    if (securityExpanded) {
    if (state.showPasswordChange) {
        AccountField(state.currentPassword, stringResource(R.string.account_current_password), "Account.CurrentPassword", password = true) {
            onEvent(AccountEvent.CurrentPassword(it))
        }
        AccountField(state.newPassword, stringResource(R.string.account_new_password), "Account.NewPassword", password = true) {
            onEvent(AccountEvent.NewPassword(it))
        }
        PasswordProgress(state.newPassword)
        if (state.currentPassword.isNotEmpty() && state.currentPassword == state.newPassword)
            Text(stringResource(R.string.ux100_platform_password_same), style = Zapara.typography.caption,
                color = Zapara.colors.bad)
        ZButton(stringResource(R.string.account_change_password), { onEvent(AccountEvent.ChangePassword) },
            enabled = enabled && state.canChangePassword, tag = "Account.ChangePassword")
    }
    if (!state.confirmLogout) {
        ZButton(stringResource(R.string.account_logout), { onEvent(AccountEvent.RequestLogout) },
            modifier = Modifier.fillMaxWidth(), ghost = true, enabled = !state.busy, tag = "Account.Logout")
    } else {
        Text(stringResource(R.string.account_confirm_logout), style = Zapara.typography.body, color = Zapara.colors.text1)
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.account_logout), { onEvent(AccountEvent.ConfirmLogout) },
                modifier = Modifier.fillMaxWidth(), enabled = !state.busy, tag = "Account.ConfirmLogout")
            ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelLogout) },
                modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Account.CancelLogout")
        }
    }
    }
    ZDisclosureButton(stringResource(R.string.account_section_data), expanded = dataOpen, onClick = {
        dataOpen = !dataOpen
        if (!dataOpen) onEvent(AccountEvent.ClearSensitive)
    }, tag = "Account.Data", quiet = true)
    if (dataOpen) {
    if (state.showPasswordProof) {
        AccountField(state.proof, stringResource(R.string.account_proof), "Account.Proof", password = true) {
            onEvent(AccountEvent.Proof(it))
        }
        if (state.proof.isNotEmpty() && !state.canPerformProtectedAction)
            Text(stringResource(R.string.ux30_platform_proof_requirements),
                style = Zapara.typography.caption, color = Zapara.colors.text2)
    } else if (state.identities.isNotEmpty()) {
        Text(stringResource(R.string.account_proof_provider), style = Zapara.typography.caption, color = Zapara.colors.text2)
    }
    ZButton(stringResource(R.string.account_export), { onEvent(AccountEvent.CreateExport) }, ghost = true,
        enabled = enabled && !state.exportPending && state.canPerformProtectedAction, tag = "Account.Export")
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
            val largeText = androidx.compose.ui.platform.LocalDensity.current.fontScale >= 1.5f
            BoxWithConstraints(Modifier.fillMaxWidth()) {
                if (largeText || maxWidth < 400.dp) {
                    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        if (state.showYandexLink) {
                            IdButton(stringResource(R.string.account_link), stringResource(R.string.face_link_yandex), R.drawable.ic_brand_yandex,
                                { onEvent(AccountEvent.LinkYandex) }, enabled, "Account.LinkYandex", Modifier.fillMaxWidth())
                        }
                        if (state.showVkLink) {
                            IdButton(stringResource(R.string.account_link), stringResource(R.string.face_link_vk), R.drawable.ic_brand_vk,
                                { onEvent(AccountEvent.LinkVk) }, enabled, "Account.LinkVk", Modifier.fillMaxWidth())
                        }
                    }
                } else {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                        if (state.showYandexLink) {
                            IdButton(stringResource(R.string.account_link), stringResource(R.string.face_link_yandex), R.drawable.ic_brand_yandex,
                                { onEvent(AccountEvent.LinkYandex) }, enabled, "Account.LinkYandex", Modifier.weight(1f))
                        }
                        if (state.showVkLink) {
                            IdButton(stringResource(R.string.account_link), stringResource(R.string.face_link_vk), R.drawable.ic_brand_vk,
                                { onEvent(AccountEvent.LinkVk) }, enabled, "Account.LinkVk", Modifier.weight(1f))
                        }
                    }
                }
            }
        }
        if (state.showYandexUnlink || state.showVkUnlink) {
            if (state.showYandexUnlink) {
                ZButton(stringResource(R.string.face_unlink_yandex, stringResource(R.string.account_unlink)), { onEvent(AccountEvent.Unlink("yandex")) }, ghost = true, enabled = enabled && state.canPerformProtectedAction, tag = "Account.UnlinkYandex")
            }
            if (state.showVkUnlink) {
                ZButton(stringResource(R.string.account_unlink) + " · VK ID", { onEvent(AccountEvent.Unlink("vk")) }, ghost = true, enabled = enabled && state.canPerformProtectedAction, tag = "Account.UnlinkVk")
            }
        }
    }
    if (!state.confirmDelete) {
        ZButton(stringResource(R.string.account_delete), { onEvent(AccountEvent.RequestDelete) }, ghost = true, enabled = enabled, tag = "Account.Delete")
    } else {
        Text(stringResource(R.string.account_delete_confirm), style = Zapara.typography.body, color = Zapara.colors.text1)
        Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
            ZButton(stringResource(R.string.account_delete), { onEvent(AccountEvent.ConfirmDelete) },
                modifier = Modifier.fillMaxWidth(), enabled = enabled && state.canPerformProtectedAction,
                destructive = true, leadingIcon = R.drawable.ic_trash, tag = "Account.ConfirmDelete")
            ZButton(stringResource(R.string.account_cancel), { onEvent(AccountEvent.CancelDelete) },
                modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Account.CancelDelete")
        }
    }
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
    val interactions = remember { MutableInteractionSource() }
    val focused by interactions.collectIsFocusedAsState()
    val pressed by interactions.collectIsPressedAsState()
    Surface(
        modifier
            .testTag(tag)
            .semantics { contentDescription = description }
            .sizeIn(minWidth = Zapara.space.minTouch, minHeight = Zapara.space.minTouch)
            .alpha(if (enabled) 1f else 0.45f)
            .then(if (enabled) Modifier.pressScale(interactions) else Modifier)
            .clip(shape)
            .controlFocusRing(enabled && focused, Zapara.colors.idInk, Zapara.radii.control)
            .clickable(enabled = enabled, interactionSource = interactions,
                indication = if (Zapara.motion.enabled) LocalIndication.current else null,
                role = Role.Button, onClick = onClick),
        shape = shape,
        color = if (pressed && enabled) Zapara.colors.idInk.copy(alpha = 0.08f).compositeOver(Color.White) else Color.White,
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
private fun PasswordProgress(value: String) {
    if (value.isEmpty()) return
    val count = value.codePointCount(0, value.length)
    val label = when {
        value.contains('\u0000') -> stringResource(R.string.ux300_android_password_invalid)
        count < 12 -> stringResource(R.string.ux300_android_password_remaining, 12 - count)
        count > 128 -> stringResource(R.string.ux300_android_password_over, count - 128)
        else -> stringResource(R.string.ux300_android_password_length_ok)
    }
    Text(label, style = Zapara.typography.caption,
        color = if (count in 12..128 && !value.contains('\u0000')) Zapara.colors.ok
            else Zapara.colors.warn)
}

@Composable
private fun AccountField(value: String, label: String, tag: String, password: Boolean = false,
    onDone: (() -> Unit)? = null, onChange: (String) -> Unit) {
    val c = Zapara.colors
    var passwordVisible by rememberSaveable(tag) { mutableStateOf(false) }
    LaunchedEffect(value.isEmpty()) { if (value.isEmpty()) passwordVisible = false }
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.xs)) {
    Text(label, style = Zapara.typography.caption, color = c.text2)
    OutlinedTextField(
        value = value,
        onValueChange = onChange,
        modifier = Modifier.fillMaxWidth().testTag(tag).semantics { contentDescription = label },
        textStyle = Zapara.typography.body,
        singleLine = true,
        visualTransformation = if (password && !passwordVisible) PasswordVisualTransformation() else VisualTransformation.None,
        keyboardOptions = KeyboardOptions(
            keyboardType = if (password) KeyboardType.Password else if (tag.contains("Username") || tag == "Account.Recovery") KeyboardType.Ascii else KeyboardType.Text,
            imeAction = if (onDone == null) ImeAction.Next else ImeAction.Done
        ),
        keyboardActions = KeyboardActions(onDone = { onDone?.invoke() }),
        trailingIcon = if (password) {{
            ZButton(stringResource(if (passwordVisible) R.string.ux30_platform_hide_password else R.string.ux30_platform_show_password),
                { passwordVisible = !passwordVisible }, ghost = true, quiet = true, tag = "$tag.Visibility")
        }} else null,
        shape = RoundedCornerShape(Zapara.radii.control),
        colors = OutlinedTextFieldDefaults.colors(
            focusedContainerColor = c.chip, unfocusedContainerColor = c.chip,
            focusedBorderColor = c.lineStrong, unfocusedBorderColor = c.chip,
            focusedTextColor = c.text1, unfocusedTextColor = c.text1
        )
    )
    }
}
