package ru.bgtu_voenmeh.zapara.ui.account

import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import ru.bgtu_voenmeh.zapara.AndroidProfileHost
import ru.bgtu_voenmeh.zapara.data.accounts.AccountExternalExchangeRequest
import ru.bgtu_voenmeh.zapara.data.accounts.AccountClientException
import ru.bgtu_voenmeh.zapara.data.accounts.AccountClientFailure
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient

internal enum class ExternalReturnResult { Ignored, Pending, SignedIn, Linked, Failed, Expired, ProfileChanged, TransitionFailed }

internal object ExternalReturn {
    private const val prefs = "zapara_external"
    private val completion = Mutex()

    fun accepts(uri: Uri): Boolean = uri.scheme == "zapara" && uri.host == "auth" && uri.path == "/external"

    fun hasPending(context: Context): Boolean = context.getSharedPreferences(prefs, Context.MODE_PRIVATE)
        .getString("transaction", null) != null

    fun remember(context: Context, transactionId: String, verifier: String, userId: String?) {
        val saved = context.getSharedPreferences(prefs, Context.MODE_PRIVATE).edit()
            .putString("transaction", transactionId)
            .putString("verifier", verifier)
            .putString("userId", userId)
            .commit()
        if (!saved) throw AccountClientException(AccountClientFailure.VaultUnavailable)
    }

    suspend fun complete(host: AndroidProfileHost, uri: Uri): ExternalReturnResult = completion.withLock {
        if (!accepts(uri)) return@withLock ExternalReturnResult.Ignored
        val id = uri.getQueryParameter("transactionId") ?: throw AccountClientException(AccountClientFailure.InvalidRequest)
        val handoff = uri.getQueryParameter("handoffCode") ?: throw AccountClientException(AccountClientFailure.InvalidRequest)
        val stored = host.app.getSharedPreferences(prefs, Context.MODE_PRIVATE)
        val expected = stored.getString("transaction", null) ?: return@withLock ExternalReturnResult.Ignored
        val verifier = stored.getString("verifier", null) ?: return@withLock ExternalReturnResult.Ignored
        if (expected != id) return@withLock ExternalReturnResult.Ignored
        finish(host, stored, id, verifier, handoff)
    }

    suspend fun resume(host: AndroidProfileHost): ExternalReturnResult = completion.withLock {
        val stored = host.app.getSharedPreferences(prefs, Context.MODE_PRIVATE)
        val id = stored.getString("transaction", null) ?: return@withLock ExternalReturnResult.Ignored
        val verifier = stored.getString("verifier", null) ?: return@withLock ExternalReturnResult.Ignored
        if (host.container.profile.userId != stored.getString("userId", null)) {
            stored.edit().clear().commit()
            return@withLock ExternalReturnResult.ProfileChanged
        }
        val client = host.accounts ?: throw AccountClientException(AccountClientFailure.NotConfigured)
        val status = try { client.externalStatus(id).status }
        catch (e: AccountClientException) {
            if (e.failure == AccountClientFailure.ExternalAttemptExpired) {
                stored.edit().clear().commit()
                return@withLock ExternalReturnResult.Expired
            }
            throw e
        }
        when (status) {
            "pending", "callbackClaimed" -> ExternalReturnResult.Pending
            "awaitingApp" -> finish(host, stored, id, verifier, "")
            "failed" -> {
                stored.edit().clear().commit()
                ExternalReturnResult.Failed
            }
            "expired" -> {
                stored.edit().clear().commit()
                ExternalReturnResult.Expired
            }
            "completed" -> {
                stored.edit().clear().commit()
                ExternalReturnResult.TransitionFailed
            }
            else -> throw AccountClientException(AccountClientFailure.InvalidPayload)
        }
    }

    private suspend fun finish(
        host: AndroidProfileHost,
        stored: SharedPreferences,
        id: String,
        verifier: String,
        handoff: String
    ): ExternalReturnResult {
        val client = host.accounts ?: throw AccountClientException(AccountClientFailure.NotConfigured)
        val expectedUser = stored.getString("userId", null)
        if (host.container.profile.userId != expectedUser) {
            stored.edit().clear().commit()
            return ExternalReturnResult.ProfileChanged
        }
        val access = if (expectedUser == null) null else {
            host.sessions?.validSession()?.accessToken
                ?: throw AccountClientException(AccountClientFailure.ReauthenticationRequired)
        }
        val exchanged = try { client.externalExchange(AccountExternalExchangeRequest(id, verifier, handoff), access) }
        catch (e: AccountClientException) {
            if (e.failure in setOf(AccountClientFailure.RegistrationUnavailable, AccountClientFailure.ExternalAttemptExpired))
                stored.edit().clear().commit()
            throw e
        }
        stored.edit().clear().commit()
        if (exchanged.status != "completed") throw AccountClientException(AccountClientFailure.InvalidPayload)
        if (host.container.profile.userId != expectedUser) {
            exchanged.session?.let { session ->
                try { client.logout(session.accessToken) } catch (_: Exception) { }
            }
            return ExternalReturnResult.ProfileChanged
        }
        if (expectedUser != null) {
            if (exchanged.session != null || exchanged.proof != null) throw AccountClientException(AccountClientFailure.InvalidPayload)
            return ExternalReturnResult.Linked
        }
        val session = exchanged.session ?: throw AccountClientException(AccountClientFailure.InvalidPayload)
        val key = host.accountScope?.key ?: throw AccountClientException(AccountClientFailure.NotConfigured)
        val committed = try { host.coordinator.commitSession(session, key).committed }
        catch (e: Exception) {
            if (host.coordinator.current.descriptor.userId != session.user.userId)
                try { client.logout(session.accessToken) } catch (_: Exception) { }
            throw e
        }
        if (!committed) {
            try { client.logout(session.accessToken) } catch (_: Exception) { }
            return ExternalReturnResult.TransitionFailed
        }
        return ExternalReturnResult.SignedIn
    }
}
