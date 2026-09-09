package ru.bgtu_voenmeh.zapara.ui.account

import android.content.Context
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
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient

class AccountViewModel(private val host: AndroidProfileHost) : ViewModel() {
    private val mutable = MutableStateFlow(
        AccountUiState(
            configured = host.accounts != null,
            guest = host.container.profile.isGuest,
            status = host.app.getString(
                if (host.accounts == null) R.string.account_unconfigured else R.string.account_guest
            )
        )
    )
    val state: StateFlow<AccountUiState> = mutable.asStateFlow()

    init {
        viewModelScope.launch {
            try {
                val caps = host.accounts?.capabilities()
                mutable.update {
                    it.copy(
                        ready = true,
                        registrationAvailable = caps?.registration == true,
                        guest = host.container.profile.isGuest,
                        status = statusText()
                    )
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaAccount", "capabilities", e)
                mutable.update { it.copy(ready = true, status = statusText()) }
            }
        }
    }

    fun onEvent(event: AccountEvent) {
        when (event) {
            is AccountEvent.Username -> mutable.update { it.copy(username = event.value) }
            is AccountEvent.Password -> mutable.update { it.copy(password = event.value) }
            is AccountEvent.DisplayName -> mutable.update { it.copy(displayName = event.value) }
            AccountEvent.ToggleRegistration -> mutable.update {
                if (!it.registrationAvailable) it else it.copy(registration = !it.registration, password = "")
            }
            AccountEvent.Submit -> submit()
            AccountEvent.RequestLogout -> mutable.update { it.copy(confirmLogout = true, password = "") }
            AccountEvent.CancelLogout -> mutable.update { it.copy(confirmLogout = false) }
            AccountEvent.ConfirmLogout -> logout()
        }
    }

    private fun submit() {
        val snap = mutable.value
        if (snap.busy || host.accounts == null) return
        viewModelScope.launch {
            mutable.update { it.copy(busy = true, password = "") }
            try {
                val client = host.accounts!!
                val secret = snap.password
                if (snap.registration) {
                    if (!snap.registrationAvailable) {
                        mutable.update { it.copy(busy = false, status = host.app.getString(R.string.account_registration_unavailable)) }
                        return@launch
                    }
                    client.register(snap.username, secret, snap.displayName.ifBlank { null })
                    mutable.update {
                        it.copy(busy = false, registration = false, status = host.app.getString(R.string.account_created))
                    }
                } else {
                    val device = AccountHttpClient.deviceId(host.app.getSharedPreferences("zapara_device", Context.MODE_PRIVATE))
                    val session = client.login(snap.username, secret, device, "Android")
                    val result = host.coordinator.commitSession(session, client.scope.key)
                    mutable.update {
                        it.copy(
                            busy = false,
                            guest = host.container.profile.isGuest,
                            accountName = session.user.username,
                            status = if (result.committed) host.app.getString(R.string.account_local) else host.app.getString(R.string.account_transition_failed)
                        )
                    }
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: AccountClientException) {
                android.util.Log.w("ZaparaAccount", "submit ${e.failure}", e)
                mutable.update { it.copy(busy = false, status = failureText(e.failure)) }
            } catch (e: Exception) {
                android.util.Log.w("ZaparaAccount", "submit", e)
                mutable.update { it.copy(busy = false, status = host.app.getString(R.string.account_failed)) }
            }
        }
    }

    private fun logout() {
        viewModelScope.launch {
            mutable.update { it.copy(busy = true, confirmLogout = false) }
            try {
                val result = host.coordinator.logout { session ->
                    host.accounts?.logout(session.accessToken)
                }
                mutable.update {
                    it.copy(
                        busy = false,
                        guest = true,
                        accountName = "",
                        status = if (result.committed) host.app.getString(R.string.account_logout_local) else host.app.getString(R.string.account_transition_failed)
                    )
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                android.util.Log.w("ZaparaAccount", "logout", e)
                mutable.update { it.copy(busy = false, status = host.app.getString(R.string.account_failed)) }
            }
        }
    }

    private fun statusText(): String {
        val app = host.app
        return when {
            host.accounts == null -> app.getString(R.string.account_unconfigured)
            host.container.profile.isGuest -> app.getString(R.string.account_guest)
            else -> app.getString(R.string.account_local)
        }
    }

    private fun failureText(failure: AccountClientFailure): String {
        val id = when (failure) {
            AccountClientFailure.InvalidCredentials -> R.string.account_bad_login
            AccountClientFailure.UsernameUnavailable -> R.string.account_username_taken
            AccountClientFailure.RateLimited -> R.string.account_rate_limited
            AccountClientFailure.NotConfigured, AccountClientFailure.RegistrationUnavailable -> R.string.account_registration_unavailable
            AccountClientFailure.ReauthenticationRequired -> R.string.account_reauth
            else -> R.string.account_failed
        }
        return host.app.getString(id)
    }

    companion object {
        fun factory(host: AndroidProfileHost) = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T = AccountViewModel(host) as T
        }
    }
}
