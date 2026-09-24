package ru.bgtu_voenmeh.zapara

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.data.Notifications
import ru.bgtu_voenmeh.zapara.data.accounts.AccountClientException
import ru.bgtu_voenmeh.zapara.data.accounts.AccountClientFailure
import ru.bgtu_voenmeh.zapara.ui.account.ExternalReturn
import ru.bgtu_voenmeh.zapara.ui.account.ExternalReturnResult
import ru.bgtu_voenmeh.zapara.ui.account.AccountViewModel
import ru.bgtu_voenmeh.zapara.ui.shell.WidgetLaunchInbox
import ru.bgtu_voenmeh.zapara.ui.shell.ZaparaApp

class MainActivity : ComponentActivity() {
    private val widgetLaunchInbox = WidgetLaunchInbox()
    private val notifPerm = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* status is reflected in Settings; re-check happens on next launch */ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        acceptLaunch(intent)
        acceptExternal(intent)
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            try { notifPerm.launch(Manifest.permission.POST_NOTIFICATIONS) } catch (_: Exception) {}
        }
        lifecycleScope.launch(Dispatchers.IO) {
            try { Notifications.schedule(applicationContext) } catch (e: Exception) {
                android.util.Log.w("ZaparaMain", "schedule", e)
            }
        }
        setContent {
            val container = (application as ZaparaApplication).container
            val launch by widgetLaunchInbox.state.collectAsState()
            ZaparaApp(container, launch = launch, onLaunchHandled = ::handleLaunch)
        }
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.RESUMED) {
                val host = (application as ZaparaApplication).host
                var retryDelay = 1_000L
                while (ExternalReturn.hasPending(host.app)) {
                    val result = finishExternal(host) { ExternalReturn.resume(host) }
                    if (result != ExternalReturnResult.Pending) break
                    delay(retryDelay)
                    retryDelay = (retryDelay * 2).coerceAtMost(10_000L)
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        acceptLaunch(intent)
        acceptExternal(intent)
    }

    private fun acceptLaunch(intent: Intent) {
        widgetLaunchInbox.accept(
            intent.getStringExtra(SECTION_EXTRA),
            intent.getStringExtra(ARGUMENT_EXTRA)
        )
    }

    private fun handleLaunch(id: Long) {
        if (widgetLaunchInbox.state.value?.id != id) return
        widgetLaunchInbox.consume(id)
        intent.removeExtra(SECTION_EXTRA)
        intent.removeExtra(ARGUMENT_EXTRA)
    }

    private fun acceptExternal(intent: Intent?) {
        val data = intent?.data ?: return
        if (!ExternalReturn.accepts(data)) return
        intent.data = null
        val host = (application as ZaparaApplication).host
        lifecycleScope.launch { finishExternal(host) { ExternalReturn.complete(host, data) } }
    }

    private suspend fun finishExternal(host: AndroidProfileHost, action: suspend () -> ExternalReturnResult): ExternalReturnResult? {
        try {
            val result = withContext(Dispatchers.IO) { action() }
            if (result != ExternalReturnResult.Ignored && result != ExternalReturnResult.Pending) {
                accountViewModel(host).externalResult(result)
                val message = when (result) {
                    ExternalReturnResult.SignedIn -> R.string.account_external_signed_in
                    ExternalReturnResult.Linked -> R.string.account_external_linked
                    ExternalReturnResult.Failed -> R.string.account_external_failed
                    ExternalReturnResult.Expired -> R.string.account_external_expired
                    ExternalReturnResult.ProfileChanged -> R.string.account_external_profile_changed
                    ExternalReturnResult.TransitionFailed -> R.string.account_transition_failed
                    is ExternalReturnResult.Verified -> R.string.account_external_verified
                    ExternalReturnResult.Ignored, ExternalReturnResult.Pending -> return result
                }
                Toast.makeText(this@MainActivity, message, Toast.LENGTH_LONG).show()
            }
            return result
        } catch (e: CancellationException) {
            throw e
        } catch (e: AccountClientException) {
            accountViewModel(host).externalFailure(e.failure)
            val message = if (e.failure == AccountClientFailure.RegistrationUnavailable)
                R.string.account_registration_unavailable else R.string.account_external_failed
            Toast.makeText(this@MainActivity, message, Toast.LENGTH_LONG).show()
            return null
        } catch (e: Exception) {
            android.util.Log.w("ZaparaMain", "external", e)
            accountViewModel(host).externalFailure(null)
            Toast.makeText(this@MainActivity, R.string.account_external_failed, Toast.LENGTH_LONG).show()
            return null
        }
    }

    private fun accountViewModel(host: AndroidProfileHost): AccountViewModel =
        ViewModelProvider(host, AccountViewModel.factory(host))[AccountViewModel::class.java]

    companion object {
        const val SECTION_EXTRA = "zapara.section"
        const val ARGUMENT_EXTRA = "zapara.argument"
    }
}
