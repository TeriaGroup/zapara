package ru.bgtu_voenmeh.zapara

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.data.Notifications
import ru.bgtu_voenmeh.zapara.ui.shell.ZaparaApp

class MainActivity : ComponentActivity() {
    private val notifPerm = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* status is reflected in Settings; re-check happens on next launch */ }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
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
            ZaparaApp(container)
        }
    }
}
