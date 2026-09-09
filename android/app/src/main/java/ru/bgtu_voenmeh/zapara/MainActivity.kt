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
import androidx.lifecycle.viewmodel.compose.viewModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import ru.bgtu_voenmeh.zapara.data.Notifications
import ru.bgtu_voenmeh.zapara.data.PublicExport
import ru.bgtu_voenmeh.zapara.ui.ScheduleViewModel
import ru.bgtu_voenmeh.zapara.ui.ScheduleVmFactory
import ru.bgtu_voenmeh.zapara.ui.ZaparaApp
import ru.bgtu_voenmeh.zapara.ui.theme.ZaparaTheme

class MainActivity : ComponentActivity() {
    private val notifPerm = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* status is reflected in Settings; re-check happens on next launch */ }
    private val storagePerm = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) {
            lifecycleScope.launch(Dispatchers.IO) {
                try { PublicExport.ensure(applicationContext) } catch (_: Exception) {}
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Ask notification permission once (Android 13+); Settings shows the status afterwards.
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            try { notifPerm.launch(Manifest.permission.POST_NOTIFICATIONS) } catch (_: Exception) {}
        }
        if (Build.VERSION.SDK_INT < 29 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.WRITE_EXTERNAL_STORAGE) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            try { storagePerm.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE) } catch (_: Exception) {}
        }
        // (Re)arm daily notification alarms; silent self-update check runs in ViewModel.init.
        lifecycleScope.launch(Dispatchers.IO) {
            try { Notifications.schedule(applicationContext) } catch (_: Exception) {}
        }
        setContent {
            ZaparaTheme {
                val vm: ScheduleViewModel = viewModel(factory = ScheduleVmFactory(application))
                ZaparaApp(vm)
            }
        }
    }
}
