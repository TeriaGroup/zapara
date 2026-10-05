package ru.bgtu_voenmeh.zapara.ui.account

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
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
import ru.bgtu_voenmeh.zapara.data.accounts.AccountExportDownload
import ru.bgtu_voenmeh.zapara.data.accounts.AccountExportJob
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient
import ru.bgtu_voenmeh.zapara.data.accounts.AccountReauthProof
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSession
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSessionManager
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
    val rememberExternal: (String, String, String?, String?, String?) -> Unit = { _, _, _, _, _ -> },
    val pendingExternal: () -> ExternalReturn.Pending? = { null },
    val cancelExternal: suspend (String) -> Boolean = { false },
    val writeExport: (ByteArray, String) -> Unit,
    val writeExportToUri: (ByteArray, Uri) -> Unit = { _, _ -> throw java.io.IOException("Document destination unavailable") },
    val capabilitiesTransport: HttpExchange?,
    val scopeBase: String?,
    val serverKey: String?,
    val sessions: AccountSessionManager? = null,
    val ioDispatcher: CoroutineDispatcher = Dispatchers.IO
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
            rememberExternal = { id, verifier, userId, purpose, provider -> ExternalReturn.remember(host.app, id, verifier, userId, purpose, provider) },
            pendingExternal = { ExternalReturn.pending(host.app) },
            cancelExternal = { id -> ExternalReturn.cancel(host.app, id) },
            openUrl = { url ->
                val parsed = Uri.parse(url)
                if (parsed.scheme != "https") throw AccountClientException(AccountClientFailure.InvalidPayload)
                try {
                    host.app.startActivity(Intent(Intent.ACTION_VIEW, parsed).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                } catch (e: Exception) {
                    android.util.Log.w("ZaparaAccount", "open", e)
                    throw AccountClientException(AccountClientFailure.Transport)
                }
            },
            writeExport = { bytes, name ->
                val dir = java.io.File(host.app.filesDir, "exports")
                if (!dir.exists()) dir.mkdirs()
                java.io.File(dir, name).writeBytes(bytes)
            },
            writeExportToUri = { bytes, uri ->
                host.app.contentResolver.openOutputStream(uri, "wt")?.use { it.write(bytes) }
                    ?: throw java.io.IOException("Cannot open export destination")
            },
            capabilitiesTransport = UrlConnectionTransport(),
            scopeBase = host.accountScope?.baseUri?.toString(),
            serverKey = host.accountScope?.key,
            sessions = host.sessions
        )
    }
}

class AccountViewModel internal constructor(private val runtime: AccountRuntime) : ViewModel() {
    constructor(host: AndroidProfileHost) : this(AccountRuntime.from(host))

    private var exportId: String? = null
    private data class PendingExportFile(val file: AccountExportDownload, val version: Long, val token: String)
    private var pendingExportFile: PendingExportFile? = null
    private var identityRevision = 0
    private var operationSerial = 0L
    private var operationClaim: Long? = null
    private var profileRefresh: kotlinx.coroutines.Job? = null
    private var capabilitiesJob: kotlinx.coroutines.Job? = null
    private val mutable = MutableStateFlow(
        AccountUiState(
            configured = runtime.client != null,
            guest = runtime.isGuest(),
            externalPending = runtime.pendingExternal() != null,
            pendingExternalProvider = runtime.pendingExternal()?.provider,
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

    init { loadCapabilities() }

    private fun loadCapabilities() {
        if (capabilitiesJob?.isActive == true) return
        mutable.update { it.copy(capabilitiesLoading = true, capabilitiesError = false) }
        capabilitiesJob = viewModelScope.launch {
            val revision = identityRevision
            try {
                val caps = when {
                    runtime.capabilitiesTransport != null && runtime.scopeBase != null ->
                        readUiCapabilities(runtime.capabilitiesTransport, runtime.scopeBase)
                    else -> AccountUiCapabilities(runtime.client?.capabilities()?.registration == true)
                }
                val guest = runtime.isGuest()
                val session = if (guest) null else try {
                    requireSession()
                } catch (e: CancellationException) {
                    throw e
                } catch (_: Exception) {
                    null
                }
                val identities = if (session != null && runtime.client != null) {
                    try {
                        runtime.client.identities(session.accessToken).map { AccountIdentityRow(it.provider) }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (_: Exception) {
                        emptyList()
                    }
                } else emptyList()
                val hasPassword = if (session != null && runtime.client != null) {
                    try {
                        "password" in runtime.client.authenticationMethods(session.accessToken)
                    } catch (e: CancellationException) {
                        throw e
                    } catch (_: Exception) {
                        null
                    }
                } else null
                mutable.update {
                    if (revision != identityRevision) return@update it.copy(ready = true, capabilitiesLoading = false)
                    it.applyCaps(caps).copy(
                        ready = true,
                        capabilitiesLoading = false,
                        capabilitiesError = false,
                        guest = guest,
                        accountName = if (guest) "" else session?.user?.accountName().orEmpty(),
                        displayName = if (guest) it.displayName else session?.user?.displayName.orEmpty(),
                        profileNameBaseline = if (guest) "" else session?.user?.displayName.orEmpty(),
                        identities = identities,
                        hasPassword = hasPassword,
                        status = statusText(guest)
                    )
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                runCatching { android.util.Log.w("ZaparaAccount", "capabilities", e) }
                if (revision == identityRevision) mutable.update { it.copy(ready = true, capabilitiesLoading = false,
                    capabilitiesError = true, guest = runtime.isGuest(), status = statusText(runtime.isGuest())) }
            }
        }
    }

    fun onEvent(event: AccountEvent) {
        when (event) {
            AccountEvent.RefreshProfile -> refreshProfile()
            AccountEvent.RetryCapabilities -> loadCapabilities()
            AccountEvent.CancelExternal -> cancelExternalAttempt()
            AccountEvent.Submit -> submit()
            AccountEvent.SaveProfile -> saveProfile()
            AccountEvent.ConfirmLogout -> logout()
            AccountEvent.LoadDevices -> loadDevices(false)
            AccountEvent.LoadMoreDevices -> if (mutable.value.deviceCursor != null) loadDevices(true)
            is AccountEvent.Revoke -> mutable.update { it.reduce(AccountEvent.RequestRevoke(event.familyId)) }
            AccountEvent.ConfirmRevoke -> mutable.value.confirmRevoke?.let { target ->
                if (target == "all") revokeAll() else revoke(target)
            }
            AccountEvent.ChangePassword -> changePassword()
            AccountEvent.CreateExport -> createExport()
            AccountEvent.CheckExport -> checkExport()
            AccountEvent.DownloadExport -> downloadExport()
            is AccountEvent.SaveExport -> saveExport(event.uri, event.version, event.token)
            AccountEvent.RetryExportSave -> pendingExportFile?.takeIf { !mutable.value.busy }?.let { pending ->
                val next = pending.copy(version = pending.version + 1, token = java.util.UUID.randomUUID().toString())
                pendingExportFile = next
                mutable.update { it.copy(exportSaveVersion = next.version, exportSaveToken = next.token) }
            }
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

    private fun refreshProfile() {
        val client = runtime.client ?: return
        if (!mutable.value.showAccount || mutable.value.busy || profileRefresh?.isActive == true) return
        val revision = identityRevision
        profileRefresh = viewModelScope.launch {
            try {
                val user = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                    val session = requireSession()
                    val fresh = client.me(session.accessToken)
                    val current = runtime.vault.acquire().use { it.read() }
                    if (current == null || current.userId != session.user.userId || current.familyId != session.familyId ||
                        fresh.userId != session.user.userId) null else fresh
                }
                if (user != null && identityRevision == revision && !runtime.isGuest())
                    mutable.update { state -> if (state.busy) state else state.copy(
                        accountName = user.accountName(),
                        displayName = if (state.displayName == state.profileNameBaseline)
                            user.displayName.orEmpty() else state.displayName,
                        profileNameBaseline = user.displayName.orEmpty()) }
            } catch (cancelled: CancellationException) { throw cancelled }
            catch (error: AccountClientException) {
                if (identityRevision == revision && error.failure in setOf(AccountClientFailure.InvalidSession, AccountClientFailure.ReauthenticationRequired))
                    mutable.update { if (it.busy) it else it.copy(status = runtime.strings(R.string.account_reauth)) }
            } catch (_: Exception) { /* A temporary outage keeps the last known profile. */ }
        }
    }

    private fun saveProfile() {
        val captured = mutable.value
        val client = runtime.client ?: return
        if (!captured.canSaveProfile) return
        val revision = identityRevision
        val draft = captured.displayName.trim()
        mutable.update { it.copy(busy = true, profileError = null) }
        viewModelScope.launch {
            try {
                val saved = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) {
                    val session = requireSession()
                    val updated = client.saveProfile(session.accessToken, draft.ifEmpty { null })
                    val active = runtime.vault.acquire().use { it.read() }
                    if (active == null || active.userId != session.user.userId ||
                        active.familyId != session.familyId || updated.userId != session.user.userId) null else updated
                }
                mutable.update { state -> if (revision != identityRevision || runtime.isGuest() || saved == null)
                    state.copy(busy = false) else state.copy(
                        busy = false, accountName = saved.accountName(),
                        displayName = if (state.displayName == captured.displayName) saved.displayName.orEmpty() else state.displayName,
                        profileNameBaseline = saved.displayName.orEmpty(), profileError = null)
                }
            } catch (e: CancellationException) { throw e }
            catch (e: Exception) {
                runCatching { android.util.Log.w("ZaparaAccount", "profile save", e) }
                mutable.update { state -> if (revision == identityRevision && !runtime.isGuest())
                    state.copy(busy = false, profileError = runtime.strings(R.string.uxnext_profile_failed))
                    else state.copy(busy = false) }
            }
        }
    }

    private fun submit() {
        val snap = mutable.value
        if (snap.busy || runtime.client == null) return
        if (!snap.canSubmitCredentials) {
            mutable.update { it.copy(status = runtime.strings(R.string.account_validation)) }
            return
        }
        launchOp(allowIdentityTransition = true) { captured ->
            val client = runtime.client!!
            val secret = captured.password
            if (captured.registration) {
                if (!captured.registrationAvailable) {
                    return@launchOp captured.copy(status = runtime.strings(R.string.account_registration_unavailable))
                }
                if (!captured.documentsAccepted) {
                    return@launchOp captured.copy(status = runtime.strings(R.string.account_accept_required))
                }
                client.register(captured.username, secret, captured.displayName.ifBlank { null })
                captured.copy(registration = false, status = runtime.strings(R.string.account_created))
            } else {
                val session = client.login(captured.username, secret, runtime.deviceId(), "Android")
                val result = runtime.commitSession(session, runtime.serverKey ?: client.scope.key)
                if (result) identityRevision++
                val identities = try {
                    client.identities(session.accessToken).map { AccountIdentityRow(it.provider) }
                } catch (_: Exception) {
                    emptyList()
                }
                captured.copy(
                    guest = runtime.isGuest(),
                    accountName = session.user.accountName(),
                    displayName = if (result) session.user.displayName.orEmpty() else captured.displayName,
                    profileNameBaseline = if (result) session.user.displayName.orEmpty() else captured.profileNameBaseline,
                    identities = identities,
                    hasPassword = if (result) true else captured.hasPassword,
                    status = if (result) runtime.strings(R.string.account_local) else runtime.strings(R.string.account_transition_failed)
                )
            }
        }
    }

    private fun logout() {
        launchOp(allowIdentityTransition = true) { captured ->
            val result = runtime.logout { session -> runtime.client?.logout(session.accessToken) }
            leave(captured, result)
        }
    }

    private fun loadDevices(append: Boolean) {
        launchOp { captured ->
            val session = requireSession()
            val cursor = if (append) captured.deviceCursor else null
            val page = runtime.client!!.listDevices(session.accessToken, cursor = cursor)
            val fetched = page.devices.map {
                AccountDeviceRow(it.familyId, it.deviceId, it.deviceName, it.platform, it.isCurrent,
                    it.createdAt, it.lastSeenAt, it.expiresAt)
            }
            captured.copy(
                devices = if (append) mergeAccountDevices(captured.devices, fetched) else fetched,
                devicesLoaded = true,
                deviceCursor = page.nextCursor?.takeUnless { it == cursor },
                status = statusText(false)
            )
        }
    }

    private fun revoke(familyId: String) {
        if (mutable.value.confirmRevoke != familyId || mutable.value.devices.none { it.familyId == familyId }) return
        launchOp(allowIdentityTransition = true) { captured ->
            val session = requireSession()
            runtime.client!!.revokeSession(session.accessToken, familyId)
            if (familyId == session.familyId) {
                leave(captured, runtime.logout(null))
            } else {
                captured.copy(devices = captured.devices.filter { it.familyId != familyId },
                    confirmRevoke = null, status = statusText(false))
            }
        }
    }

    private fun revokeAll() {
        if (mutable.value.confirmRevoke != "all" || mutable.value.devices.isEmpty()) return
        launchOp(allowIdentityTransition = true) { captured ->
            val session = requireSession()
            runtime.client!!.revokeAll(session.accessToken)
            leave(captured, runtime.logout(null))
        }
    }

    private fun changePassword() {
        launchOp(allowIdentityTransition = true) { captured ->
            val session = requireSession()
            runtime.client!!.changePassword(session.accessToken, captured.currentPassword, captured.newPassword)
            leave(captured, runtime.logout(null))
        }
    }

    private fun createExport() {
        if (exportId != null && mutable.value.exportPending) return
        val revision = identityRevision
        launchOp { captured ->
            if (captured.hasPassword != true) return@launchOp startProviderProof(captured, "export")
            val session = requireSession()
            val client = runtime.client!!
            val issued = client.reauthenticate(session.accessToken, captured.proof, "export")
            val job = client.createExport(session.accessToken, issued.proofToken)
            finishExportCreation(captured, job, session.accessToken, revision)
        }
    }

    private suspend fun finishExportCreation(captured: AccountUiState, created: AccountExportJob,
        accessToken: String, revision: Int): AccountUiState {
        if (revision != identityRevision || runtime.isGuest()) return captured
        exportId = created.exportId // POST acknowledged: a failed status GET must never issue a second job.
        if (created.status == "ready" || created.status !in setOf("pending", "preparing", "processing"))
            return exportJobState(captured, created)
        return try {
            val checked = runtime.client!!.getExport(accessToken, created.exportId)
            if (checked.exportId != created.exportId) throw AccountClientException(AccountClientFailure.InvalidPayload)
            if (revision != identityRevision || runtime.isGuest()) captured
            else exportJobState(captured, checked)
        } catch (e: CancellationException) { throw e }
        catch (_: Exception) { captured.copy(exportReady = false, exportPending = true,
            status = runtime.strings(R.string.ux60_export_check_failed)) }
    }

    private fun exportJobState(captured: AccountUiState, job: AccountExportJob): AccountUiState {
        exportId = job.exportId
        return when (job.status) {
            "ready" -> captured.copy(exportReady = true, exportPending = false,
                status = runtime.strings(R.string.ux60_export_ready))
            "pending", "preparing", "processing" -> captured.copy(exportReady = false, exportPending = true,
                status = runtime.strings(R.string.ux60_export_pending))
            else -> {
                exportId = null
                captured.copy(exportReady = false, exportPending = false,
                    status = runtime.strings(R.string.ux60_export_failed))
            }
        }
    }

    private suspend fun exportOwnerCurrent(session: AccountSession, revision: Int, id: String): Boolean {
        if (revision != identityRevision || runtime.isGuest() || exportId != id) return false
        val active = runtime.vault.acquire().use { it.read() }
        return active?.userId == session.user.userId && active.familyId == session.familyId
    }

    private fun releaseMissingExport(captured: AccountUiState): AccountUiState {
        exportId = null
        pendingExportFile = null
        return captured.copy(exportPending = false, exportReady = false,
            exportSaveName = null, exportSaveToken = null,
            status = runtime.strings(R.string.ux60_export_expired))
    }

    private fun checkExport() {
        val id = exportId ?: return
        if (!mutable.value.exportPending) return
        val revision = identityRevision
        launchOp { captured ->
            val session = requireSession()
            val job = try { runtime.client!!.getExport(session.accessToken, id) }
                catch (e: AccountClientException) {
                    if (e.failure == AccountClientFailure.ExportNotFound && exportOwnerCurrent(session, revision, id))
                        return@launchOp releaseMissingExport(captured)
                    throw e
                }
            if (job.exportId != id) throw AccountClientException(AccountClientFailure.InvalidPayload)
            if (revision != identityRevision || runtime.isGuest()) return@launchOp captured
            exportJobState(captured, job)
        }
    }

    private fun downloadExport() {
        if (!mutable.value.exportReady || pendingExportFile != null || mutable.value.exportSaveName != null) return
        val revision = identityRevision
        launchOp { captured ->
            val id = exportId ?: throw AccountClientException(AccountClientFailure.ExportNotFound)
            val session = requireSession()
            val file = try { runtime.client!!.downloadExport(session.accessToken, id) }
                catch (e: AccountClientException) {
                    if (e.failure == AccountClientFailure.ExportNotFound && exportOwnerCurrent(session, revision, id))
                        return@launchOp releaseMissingExport(captured)
                    throw e
                }
            if (revision != identityRevision || runtime.isGuest()) return@launchOp captured
            val version = captured.exportSaveVersion + 1
            val token = java.util.UUID.randomUUID().toString()
            pendingExportFile = PendingExportFile(file, version, token)
            captured.copy(exportSaveName = file.fileName,
                exportSaveVersion = version,
                exportSaveToken = token,
                status = runtime.strings(R.string.ux60_export_choose_location))
        }
    }

    private fun saveExport(uri: Uri?, version: Long, token: String) {
        val pending = pendingExportFile ?: return
        if (pending.version != version || pending.token != token ||
            mutable.value.exportSaveVersion != version || mutable.value.exportSaveToken != token) return
        val file = pending.file
        if (uri == null) {
            pendingExportFile = null
            mutable.update { it.copy(exportSaveName = null, exportSaveToken = null,
                status = runtime.strings(R.string.ux60_export_save_cancelled)) }
            return
        }
        launchOp { captured ->
            try {
                withContext(runtime.ioDispatcher) { runtime.writeExportToUri(file.bytes, uri) }
                if (pendingExportFile === pending) pendingExportFile = null
                captured.copy(exportSaveName = null, exportSaveToken = null,
                    status = runtime.strings(R.string.account_export_download))
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) {
                captured.copy(exportSaveName = file.fileName,
                    status = runtime.strings(R.string.ux60_export_save_failed))
            }
        }
    }

    private fun deleteAccount() {
        if (!mutable.value.confirmDelete) return
        launchOp(allowIdentityTransition = true) { captured ->
            if (captured.hasPassword != true) return@launchOp startProviderProof(captured, "delete_account")
            val session = requireSession()
            val client = runtime.client!!
            val issued = client.reauthenticate(session.accessToken, captured.proof, "delete_account")
            client.deleteAccount(session.accessToken, issued.proofToken)
            leave(captured, runtime.logout(null))
        }
    }

    private fun requestReset() {
        if (!mutable.value.canRequestRecovery) {
            mutable.update { it.copy(status = runtime.strings(R.string.account_validation)) }
            return
        }
        launchOp { captured ->
            val username = captured.recoveryUsername.ifBlank { captured.username }.trim()
            runtime.client!!.requestPasswordReset(username)
            captured.copy(recoveryStep = AccountRecoveryStep.Confirm,
                status = runtime.strings(R.string.ux30_recovery_requested))
        }
    }

    private fun confirmReset() {
        if (mutable.value.recoveryStep != AccountRecoveryStep.Confirm || !mutable.value.canConfirmRecovery) return
        launchOp { captured ->
            runtime.client!!.confirmPasswordReset(captured.proof, captured.newPassword)
            captured.copy(recoveryStep = AccountRecoveryStep.Request,
                username = captured.recoveryUsername.ifBlank { captured.username }.trim(), registration = false,
                recoveryCompletionVersion = captured.recoveryCompletionVersion + 1,
                status = runtime.strings(R.string.ux30_recovery_done)).clearSecrets()
        }
    }

    private fun startExternal(provider: String, login: Boolean) {
        val snap = mutable.value
        if (runtime.pendingExternal() != null || snap.externalPending) {
            mutable.update { it.copy(status = runtime.strings(R.string.ux60_account_external_pending)) }
            return
        }
        if (login && snap.registration && !snap.documentsAccepted) {
            mutable.update { it.copy(status = runtime.strings(R.string.account_accept_required)) }
            return
        }
        if (login && !snap.guest) return
        if (!login && snap.guest) return
        launchOp(provider) { captured ->
            if (!login && captured.hasPassword != true) return@launchOp startProviderProof(captured, "link:$provider")
            val client = runtime.client!!
            val pkce = nativePkce()
            val current = if (login) null else requireSession()
            val access = current?.accessToken
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
            runtime.rememberExternal(start.transactionId, pkce.verifier, current?.user?.userId, null, provider)
            mutable.update { it.copy(externalPending = true, pendingExternalProvider = provider) }
            runtime.openUrl(start.authorizeUrl)
            captured.copy(status = runtime.strings(R.string.account_external_pending), guest = runtime.isGuest(),
                externalPending = true, pendingExternalProvider = provider)
        }
    }

    private fun cancelExternalAttempt() {
        val pending = runtime.pendingExternal()
        if (pending == null) {
            mutable.update { it.copy(externalPending = false, pendingExternalProvider = null) }
            return
        }
        if (mutable.value.busy) return
        mutable.update { it.copy(busy = true) }
        viewModelScope.launch {
            try {
                val cancelled = runtime.cancelExternal(pending.transactionId)
                val current = runtime.pendingExternal()
                mutable.update { it.copy(busy = false, externalPending = current != null,
                    pendingExternalProvider = current?.provider,
                    status = runtime.strings(if (cancelled) R.string.ux60_account_external_cancelled
                        else R.string.ux60_account_external_pending)) }
            } catch (e: CancellationException) { throw e }
            catch (_: Exception) {
                mutable.update { it.copy(busy = false, status = runtime.strings(R.string.account_failed)) }
            }
        }
    }

    private fun unlink(provider: String) {
        if (provider != "vk" && provider != "yandex") return
        launchOp(provider) { captured ->
            if (captured.hasPassword != true) {
                if (captured.identities.size <= 1) throw AccountClientException(AccountClientFailure.InvalidRequest)
                return@launchOp startProviderProof(captured, "unlink:$provider", exclude = provider)
            }
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

    private suspend fun startProviderProof(captured: AccountUiState, purpose: String, exclude: String? = null): AccountUiState {
        if (runtime.pendingExternal() != null) return captured.copy(status = runtime.strings(R.string.ux60_account_external_pending))
        val provider = captured.identities.map { it.provider }.firstOrNull {
            it != exclude && ((it == "yandex" && captured.yandexAvailable) || (it == "vk" && captured.vkAvailable))
        } ?: throw AccountClientException(AccountClientFailure.ProviderUnavailable)
        val session = requireSession()
        val client = runtime.client ?: throw AccountClientException(AccountClientFailure.NotConfigured)
        val pkce = nativePkce()
        val start = client.externalStart(provider, AccountExternalStartRequest(
            purpose = "reauth", nativeChallenge = pkce.challenge, nativeChallengeMethod = "S256",
            deviceId = runtime.deviceId(), deviceName = "Android", platform = "android", returnKind = "android",
            proofPurpose = purpose
        ), session.accessToken)
        runtime.rememberExternal(start.transactionId, pkce.verifier, session.user.userId, purpose, provider)
        mutable.update { it.copy(externalPending = true, pendingExternalProvider = provider) }
        runtime.openUrl(start.authorizeUrl)
        return captured.copy(status = runtime.strings(R.string.account_external_pending),
            externalPending = true, pendingExternalProvider = provider)
    }

    private fun finishProviderProof(proof: AccountReauthProof) {
        val revision = identityRevision
        launchOp(allowIdentityTransition = true) { captured ->
            val session = requireSession()
            val client = runtime.client ?: throw AccountClientException(AccountClientFailure.NotConfigured)
            when (proof.purpose) {
                "export" -> {
                    val job = client.createExport(session.accessToken, proof.proofToken)
                    finishExportCreation(captured, job, session.accessToken, revision)
                }
                "delete_account" -> {
                    if (!captured.confirmDelete) return@launchOp captured
                    client.deleteAccount(session.accessToken, proof.proofToken)
                    leave(captured, runtime.logout(null))
                }
                "link:vk", "link:yandex" -> {
                    val provider = proof.purpose.substringAfter(':')
                    val pkce = nativePkce()
                    val start = client.externalStart(provider, AccountExternalStartRequest(
                        purpose = "link", nativeChallenge = pkce.challenge, nativeChallengeMethod = "S256",
                        deviceId = runtime.deviceId(), deviceName = "Android", platform = "android", returnKind = "android",
                        proofToken = proof.proofToken
                    ), session.accessToken)
                    runtime.rememberExternal(start.transactionId, pkce.verifier, session.user.userId, null, provider)
                    mutable.update { it.copy(externalPending = true, pendingExternalProvider = provider) }
                    runtime.openUrl(start.authorizeUrl)
                    captured.copy(status = runtime.strings(R.string.account_external_pending),
                        externalPending = true, pendingExternalProvider = provider)
                }
                "unlink:vk", "unlink:yandex" -> {
                    val provider = proof.purpose.substringAfter(':')
                    client.unlinkIdentity(session.accessToken, provider, proof.proofToken)
                    captured.copy(identities = captured.identities.filter { it.provider != provider }, status = statusText(false))
                }
                else -> throw AccountClientException(AccountClientFailure.InvalidPayload)
            }
        }
    }

    private data class OwnerStamp(val revision: Int, val guest: Boolean, val userId: String?, val familyId: String?)

    private suspend fun ownerStamp(revision: Int, guest: Boolean): OwnerStamp {
        return withContext(runtime.ioDispatcher) {
            val entry = runtime.vault.acquire().use { it.read() }
            OwnerStamp(revision, guest, entry?.userId, entry?.familyId)
        }
    }

    private suspend fun ownerCurrent(stamp: OwnerStamp): Boolean {
        if (stamp.revision != identityRevision || stamp.guest != runtime.isGuest()) return false
        val current = ownerStamp(stamp.revision, stamp.guest)
        return current.userId == stamp.userId && current.familyId == stamp.familyId
    }

    private fun launchOp(provider: String? = null, allowIdentityTransition: Boolean = false,
        block: suspend (AccountUiState) -> AccountUiState) {
        val snap = mutable.value
        if (snap.busy || operationClaim != null || runtime.client == null) return
        val revision = identityRevision
        val guest = runtime.isGuest()
        val claim = ++operationSerial
        operationClaim = claim
        mutable.update { it.copy(busy = true).clearSecrets() }
        viewModelScope.launch {
            var owner: OwnerStamp? = null
            try {
                owner = try { ownerStamp(revision, guest) } catch (e: CancellationException) { throw e }
                    catch (_: Exception) { null }
                val stamp = owner
                if (stamp == null) {
                    if (revision == identityRevision) mutable.update { it.copy(busy = false, status = runtime.strings(R.string.account_failed)) }
                    return@launch
                }
                if (!ownerCurrent(stamp)) return@launch
                val result = block(snap)
                if (!allowIdentityTransition && !ownerCurrent(stamp)) return@launch
                mutable.update { result.copy(busy = false).clearSecrets() }
            } catch (e: CancellationException) {
                throw e
            } catch (e: AccountClientException) {
                runCatching { android.util.Log.w("ZaparaAccount", "op ${e.failure}", e) }
                if (owner?.let { ownerCurrent(it) } == true) mutable.update { it.copy(busy = false, status = failureText(e.failure)).hide(e.failure, provider) }
            } catch (e: IllegalArgumentException) {
                if (owner?.let { ownerCurrent(it) } == true) mutable.update { it.copy(busy = false, status = runtime.strings(R.string.account_validation)) }
            } catch (e: Exception) {
                runCatching { android.util.Log.w("ZaparaAccount", "op", e) }
                if (owner?.let { ownerCurrent(it) } == true) mutable.update { it.copy(busy = false, status = runtime.strings(R.string.account_failed)) }
            } finally { if (operationClaim == claim) operationClaim = null }
        }
    }

    private suspend fun requireSession(): AccountSession {
        runtime.sessions?.let { return it.validSession() }
        return runtime.vault.acquire().use { it.read()?.session }
            ?: throw AccountClientException(AccountClientFailure.ReauthenticationRequired)
    }

    private fun leave(from: AccountUiState, ok: Boolean): AccountUiState {
        if (!ok || !runtime.isGuest()) return from.copy(
            confirmLogout = false, confirmDelete = false, confirmRevoke = null,
            status = runtime.strings(R.string.account_transition_failed)
        ).clearSecrets()
        identityRevision++
        exportId = null
        pendingExportFile = null
        return from.copy(
            guest = true,
            accountName = "",
            displayName = "",
            profileNameBaseline = "",
            profileError = null,
            devices = emptyList(),
            devicesLoaded = false,
            deviceCursor = null,
            confirmRevoke = null,
            identities = emptyList(),
            hasPassword = null,
            exportReady = false,
            exportPending = false,
            exportSaveName = null,
            exportSaveToken = null,
            confirmDelete = false,
            confirmLogout = false,
            status = runtime.strings(R.string.account_logout_local)
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
            AccountClientFailure.NotConfigured, AccountClientFailure.ProviderUnavailable -> R.string.account_unconfigured
            AccountClientFailure.RegistrationUnavailable -> R.string.account_registration_unavailable
            AccountClientFailure.ExternalAttemptExpired -> R.string.account_external_expired
            AccountClientFailure.ReauthenticationRequired, AccountClientFailure.InvalidSession, AccountClientFailure.InvalidExternalProof -> R.string.account_reauth
            else -> R.string.account_failed
        }
        return runtime.strings(id)
    }

    internal fun externalResult(result: ExternalReturnResult) {
        if (result != ExternalReturnResult.Ignored && result != ExternalReturnResult.Pending)
            mutable.update { operationClaim = null; it.copy(externalPending = false, pendingExternalProvider = null, busy = false) }
        when (result) {
            is ExternalReturnResult.Verified -> finishProviderProof(result.proof)
            ExternalReturnResult.SignedIn -> {
                identityRevision++
                exportId = null
                pendingExportFile = null
                val revision = identityRevision
                viewModelScope.launch {
                    try {
                        val session = requireSession()
                        val client = runtime.client ?: return@launch
                        val identities = try { client.identities(session.accessToken).map { AccountIdentityRow(it.provider) } }
                        catch (e: CancellationException) { throw e }
                        catch (_: Exception) { emptyList() }
                        val hasPassword = try { "password" in client.authenticationMethods(session.accessToken) }
                        catch (e: CancellationException) { throw e }
                        catch (_: Exception) { null }
                        if (revision != identityRevision || runtime.isGuest()) return@launch
                        mutable.update { it.copy(
                            ready = true, guest = runtime.isGuest(), registration = false,
                            accountName = session.user.accountName(), identities = identities,
                            displayName = session.user.displayName.orEmpty(),
                            profileNameBaseline = session.user.displayName.orEmpty(),
                            hasPassword = hasPassword, devices = emptyList(), devicesLoaded = false,
                            exportReady = false, exportPending = false, exportSaveName = null,
                            exportSaveToken = null,
                            status = runtime.strings(R.string.account_external_signed_in)
                        ) }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: AccountClientException) {
                        if (revision == identityRevision) mutable.update { it.copy(status = failureText(e.failure)) }
                    } catch (_: Exception) {
                        if (revision == identityRevision) mutable.update { it.copy(status = runtime.strings(R.string.account_reauth)) }
                    }
                }
            }
            ExternalReturnResult.Linked -> {
                launchOp { captured ->
                    val session = requireSession()
                    captured.copy(
                        identities = runtime.client!!.identities(session.accessToken).map { AccountIdentityRow(it.provider) },
                        status = runtime.strings(R.string.account_external_linked)
                    )
                }
            }
            ExternalReturnResult.ProfileChanged -> mutable.update { it.copy(status = runtime.strings(R.string.account_external_profile_changed)) }
            ExternalReturnResult.Failed -> mutable.update { it.copy(status = runtime.strings(R.string.account_external_failed)) }
            ExternalReturnResult.Expired -> mutable.update { it.copy(status = runtime.strings(R.string.account_external_expired)) }
            ExternalReturnResult.TransitionFailed -> mutable.update { it.copy(status = runtime.strings(R.string.account_transition_failed)) }
            else -> Unit
        }
    }

    fun externalFailure(failure: AccountClientFailure?) {
        val message = when (failure) {
            AccountClientFailure.InvalidExternalProof -> runtime.strings(R.string.account_external_failed)
            null -> runtime.strings(R.string.account_external_failed)
            else -> failureText(failure)
        }
        mutable.update { it.copy(status = message) }
    }

    companion object {
        fun factory(host: AndroidProfileHost) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = AccountViewModel(host) as T
        }
    }
}

private fun ru.bgtu_voenmeh.zapara.data.accounts.AccountUser.accountName(): String =
    displayName?.takeIf { it.isNotBlank() } ?: username
