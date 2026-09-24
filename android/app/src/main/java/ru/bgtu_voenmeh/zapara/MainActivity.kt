package ru.bgtu_voenmeh.zapara

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.data.Notifications
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
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        acceptLaunch(intent)
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

    companion object {
        const val SECTION_EXTRA = "zapara.section"
        const val ARGUMENT_EXTRA = "zapara.argument"
    }
}
