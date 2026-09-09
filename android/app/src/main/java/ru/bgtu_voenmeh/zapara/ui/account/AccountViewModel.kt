package ru.bgtu_voenmeh.zapara.ui.account

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.AndroidProfileHost
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.accounts.AccountClientException
import ru.bgtu_voenmeh.zapara.data.accounts.AccountClientFailure
import ru.bgtu_voenmeh.zapara.data.accounts.AccountExternalStartRequest
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSession
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSessionVault
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport

internal class AccountRuntime(
    val client: AccountHttpClient?,
    val vault: AccountSessionVault,
    val strings: (Int) -> String,
    val deviceId: () -> String,
    val isGuest: () -> Boolean,
    val commitSession: suspend (AccountSession, String) -> Boolean,
    val logout: suspend (remote: (suspend (AccountSession) -> Unit)?) -> Boolean,
    val openUrl: (String) -> Unit,
    val writeExport: (ByteArray, String) -> Unit,
    val capabilitiesTransport: HttpExchange?,
    val scopeBase: String?,
    val serverKey: String?
) {
    companion object {
        fun from(host: AndroidProfileHost) = AccountRuntime(
            client = host.accounts,
            vault = host.vault,
            strings = { host.app.getString(it) },
            deviceId = {
                AccountHttpClient.deviceId(host.app.getSharedPreferences("zapara_device", Context.MODE_PRIVATE))
            },
            isGuest = { host.container.profile.isGuest },
            commitSession = { session, key -> host.coordinator.commitSession(session, key).committed },
            logout = { remote -> host.coordinator.logout(remote).committed },
            openUrl = { url ->
                val parsed = Uri.parse(url)
                if (parsed.scheme == "https" || parsed.scheme == "http") {
                    try {
                        host.app.startActivity(Intent(Intent.ACTION_VIEW, parsed).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                    } catch (e: Exception) {
                        android.util.Log.w("ZaparaAccount", "open", e)
                    }
                }
            },
            writeExport = { bytes, name ->
                val dir = java.io.File(host.app.filesDir, "exports")
                if (!dir.exists()) dir.mkdirs()
                java.io.File(dir, name).writeBytes(bytes)
            },
            capabilitiesTransport = UrlConnectionTransport(),
            scopeBase = host.accountScope?.baseUri?.toString(),
            serverKey = host.accountScope?.key
        )
    }
}

class AccountViewModel internal constructor(private val runtime: AccountRuntime) : ViewModel() {
    constructor(host: AndroidProfileHost) : this(AccountRuntime.from(host))

    private var exportId: String? = null
    private val mutable = MutableStateFlow(
        AccountUiState(
            configured = runtime.client != null,
            guest = runtime.isGuest(),
            status = runtime.strings(
                when {
                    runtime.client == null -> R.string.account_unconfigured
                    runtime.isGuest() -> R.string.account_guest
                    else -> R.string.account_local
                }
            )
        )
    )
    val state: StateFlow<AccountUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch {
            try {
                val caps = when {
                    runtime.capabilitiesTransport != null && runtime.scopeBase != null ->
                        readUiCapabilities(runtime.capabilitiesTransport, runtime.scopeBase)
                    else -> AccountUiCapabilities(runtime.client?.capabilities()?.registration == true)
                }
                val entry = runtime.vault.acquire().use { it.read() }
                val guest = runtime.isGuest()
                val identities = if (!guest && runtime.client != null && entry != null) {
                    try {
                        runtime.client.identities(entry.session.accessToken).map { AccountIdentityRow(it.provider) }
                    } catch (_: Exception) {
                        emptyList()
                    }
                } else emptyList()
                mutable.update {
                    it.applyCaps(caps).copy(
                        ready = true,
                        guest = guest,
                        accountName = if (guest) "" else (entry?.session?.user?.username ?: it.accountName),
                        identities = identities,
                        status = statusText(guest)
                    )
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaAccount", "capabilities", e)
                mutable.update { it.copy(ready = true, guest = runtime.isGuest(), status = statusText(runtime.isGuest())) }
            }
        }
    }

    fun onEvent(event: AccountEvent) {
        when (event) {
            AccountEvent.Submit -> submit()
            AccountEvent.ConfirmLogout -> logout()
            AccountEvent.LoadDevices -> loadDevices()
            is AccountEvent.Revoke -> revoke(event.familyId)
            AccountEvent.ChangePassword -> changePassword()
            AccountEvent.CreateExport -> createExport()
            AccountEvent.DownloadExport -> downloadExport()
            AccountEvent.ConfirmDelete -> deleteAccount()
            AccountEvent.RequestReset -> requestReset()
            AccountEvent.ConfirmReset -> confirmReset()
            AccountEvent.StartVk -> startExternal("vk", login = true)
            AccountEvent.StartYandex -> startExternal("yandex", login = true)
            AccountEvent.LinkVk -> startExternal("vk", login = false)
            AccountEvent.LinkYandex -> startExternal("yandex", login = false)
            is AccountEvent.Unlink -> unlink(event.provider)
            else -> mutable.update { it.reduce(event) }
        }
    }

    private fun submit() {
        val snap = mutable.value
        if (snap.busy || runtime.client == null) return
        launchOp { captured ->
            val client = runtime.client!!
            val secret = captured.password
            if (captured.registration) {
                if (!captured.registrationAvailable) {
                    return@launchOp captured.copy(status = runtime.strings(R.string.account_registration_unavailable))
                }
                client.register(captured.username, secret, captured.displayName.ifBlank { null })
                captured.copy(registration = false, status = runtime.strings(R.string.account_created))
            } else {
                val session = client.login(captured.username, secret, runtime.deviceId(), "Android")
                val result = runtime.commitSession(session, runtime.serverKey ?: client.scope.key)
                val identities = try {
                    client.identities(session.accessToken).map { AccountIdentityRow(it.provider) }
                } catch (_: Exception) {
                    emptyList()
                }
                captured.copy(
                    guest = runtime.isGuest(),
                    accountName = session.user.username,
                    identities = identities,
                    status = if (result) runtime.strings(R.string.account_local) else runtime.strings(R.string.account_transition_failed)
                )
            }
        }
    }

    private fun logout() {
        launchOp { captured ->
            val result = runtime.logout { session -> runtime.client?.logout(session.accessToken) }
            leave(captured, result)
        }
    }

    private fun loadDevices() {
        launchOp { captured ->
            val session = requireSession()
            val page = runtime.client!!.listDevices(session.accessToken)
            captured.copy(
                devices = page.devices.map {
                    AccountDeviceRow(it.familyId, it.deviceId, it.deviceName, it.platform, it.isCurrent)
                },
                status = statusText(false)
            )
        }
    }

    private fun revoke(familyId: String) {
        launchOp { captured ->
            val session = requireSession()
            runtime.client!!.revokeSession(session.accessToken, familyId)
            if (familyId == session.familyId) {
                leave(captured, runtime.logout(null))
            } else {
                captured.copy(devices = captured.devices.filter { it.familyId != familyId }, status = statusText(false))
            }
        }
    }

    private fun changePassword() {
        launchOp { captured ->
            val session = requireSession()
            runtime.client!!.changePassword(session.accessToken, captured.currentPassword, captured.newPassword)
            leave(captured, runtime.logout(null))
        }
    }

    private fun createExport() {
        launchOp { captured ->
            val session = requireSession()
            val client = runtime.client!!
            val issued = client.reauthenticate(session.accessToken, captured.proof, "export")
            var job = client.createExport(session.accessToken, issued.proofToken)
            if (job.status != "ready") job = client.getExport(session.accessToken, job.exportId)
            exportId = job.exportId
            captured.copy(exportReady = job.status == "ready", status = statusText(false))
        }
    }

    private fun downloadExport() {
        launchOp { captured ->
            val id = exportId ?: throw AccountClientException(AccountClientFailure.ExportNotFound)
            val session = requireSession()
            val file = runtime.client!!.downloadExport(session.accessToken, id)
            runtime.writeExport(file.bytes, file.fileName)
            captured.copy(exportReady = true, status = runtime.strings(R.string.account_export_download))
        }
    }

    private fun deleteAccount() {
        if (!mutable.value.confirmDelete) return
        launchOp { captured ->
            val session = requireSession()
            val client = runtime.client!!
            val issued = client.reauthenticate(session.accessToken, captured.proof, "delete_account")
            client.deleteAccount(session.accessToken, issued.proofToken)
            leave(captured, runtime.logout(null))
        }
    }

    private fun requestReset() {
        launchOp { captured ->
            val username = captured.recoveryUsername.ifBlank { captured.username }
            runtime.client!!.requestPasswordReset(username)
            captured.copy(status = statusText(true))
        }
    }

    private fun confirmReset() {
        launchOp { captured ->
            runtime.client!!.confirmPasswordReset(captured.proof, captured.newPassword)
            captured.copy(status = statusText(true))
        }
    }

    private fun startExternal(provider: String, login: Boolean) {
        val snap = mutable.value
        if (login && !snap.guest) return
        if (!login && snap.guest) return
        launchOp(provider) { captured ->
            val client = runtime.client!!
            val pkce = nativePkce()
            val access = if (login) null else requireSession().accessToken
            val issued = if (login) null else client.reauthenticate(access!!, captured.proof, "link:$provider")
            val start = client.externalStart(
                provider,
                AccountExternalStartRequest(
                    purpose = if (login) "login" else "link",
                    nativeChallenge = pkce.challenge,
                    nativeChallengeMethod = "S256",
                    deviceId = runtime.deviceId(),
                    deviceName = "Android",
                    platform = "android",
                    returnKind = "android",
                    proofToken = issued?.proofToken
                ),
                accessToken = access
            )
            runtime.openUrl(start.authorizeUrl)
            captured.copy(status = statusText(captured.guest), guest = runtime.isGuest())
        }
    }

    private fun unlink(provider: String) {
        if (provider != "vk" && provider != "yandex") return
        launchOp(provider) { captured ->
            val session = requireSession()
            val client = runtime.client!!
            val issued = client.reauthenticate(session.accessToken, captured.proof, "unlink:$provider")
            client.unlinkIdentity(session.accessToken, provider, issued.proofToken)
            captured.copy(
                identities = captured.identities.filter { it.provider != provider },
                status = statusText(false)
            )
        }
    }

    private fun launchOp(provider: String? = null, block: suspend (AccountUiState) -> AccountUiState) {
        val snap = mutable.value
        if (snap.busy || runtime.client == null) return
        viewModelScope.launch {
            mutable.update { it.copy(busy = true).clearSecrets() }
            try {
                mutable.update { block(snap).copy(busy = false).clearSecrets() }
            } catch (e: CancellationException) {
                throw e
            } catch (e: AccountClientException) {
                android.util.Log.w("ZaparaAccount", "op ${e.failure}", e)
                mutable.update { it.copy(busy = false, status = failureText(e.failure)).hide(e.failure, provider) }
            } catch (e: IllegalArgumentException) {
                mutable.update { it.copy(busy = false, status = runtime.strings(R.string.account_validation)) }
            } catch (e: Exception) {
                android.util.Log.w("ZaparaAccount", "op", e)
                mutable.update { it.copy(busy = false, status = runtime.strings(R.string.account_failed)) }
            }
        }
    }

    private suspend fun requireSession(): AccountSession =
        runtime.vault.acquire().use { it.read()?.session }
            ?: throw AccountClientException(AccountClientFailure.ReauthenticationRequired)

    private fun leave(from: AccountUiState, ok: Boolean): AccountUiState {
        exportId = null
        return from.copy(
            guest = true,
            accountName = "",
            devices = emptyList(),
            identities = emptyList(),
            exportReady = false,
            confirmDelete = false,
            confirmLogout = false,
            status = runtime.strings(if (ok) R.string.account_logout_local else R.string.account_transition_failed)
        )
    }

    private fun AccountUiState.hide(failure: AccountClientFailure, provider: String?): AccountUiState = when (failure) {
        AccountClientFailure.ProviderUnavailable -> when (provider) {
            "vk" -> copy(vkAvailable = false)
            "yandex" -> copy(yandexAvailable = false)
            else -> this
        }
        AccountClientFailure.RecoveryUnavailable -> copy(recoveryAvailable = false)
        else -> this
    }

    private fun statusText(guest: Boolean = runtime.isGuest()): String = runtime.strings(
        when {
            runtime.client == null -> R.string.account_unconfigured
            guest -> R.string.account_guest
            else -> R.string.account_local
        }
    )

    private fun failureText(failure: AccountClientFailure): String {
        val id = when (failure) {
            AccountClientFailure.InvalidCredentials -> R.string.account_bad_login
            AccountClientFailure.UsernameUnavailable -> R.string.account_username_taken
            AccountClientFailure.RateLimited -> R.string.account_rate_limited
            AccountClientFailure.NotConfigured, AccountClientFailure.RegistrationUnavailable -> R.string.account_registration_unavailable
            AccountClientFailure.ReauthenticationRequired, AccountClientFailure.InvalidSession, AccountClientFailure.InvalidExternalProof -> R.string.account_reauth
            else -> R.string.account_failed
        }
        return runtime.strings(id)
    }

    companion object {
        fun factory(host: AndroidProfileHost) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = AccountViewModel(host) as T
        }
    }
}
