package ru.bgtu_voenmeh.zapara.ui.account

import android.content.Context
import android.net.Uri
import ru.bgtu_voenmeh.zapara.AndroidProfileHost
import ru.bgtu_voenmeh.zapara.data.accounts.AccountExternalExchangeRequest

internal object ExternalReturn {
    private const val prefs = "zapara_external"

    fun remember(context: Context, transactionId: String, verifier: String) {
        context.getSharedPreferences(prefs, Context.MODE_PRIVATE).edit()
            .putString("transaction", transactionId)
            .putString("verifier", verifier)
            .apply()
    }

    suspend fun complete(host: AndroidProfileHost, uri: Uri) {
        if (uri.scheme != "zapara" || uri.host != "auth" || uri.path != "/external") return
        val id = uri.getQueryParameter("transactionId") ?: return
        val handoff = uri.getQueryParameter("handoffCode") ?: return
        val client = host.accounts ?: return
        val stored = host.app.getSharedPreferences(prefs, Context.MODE_PRIVATE)
        val expected = stored.getString("transaction", null) ?: return
        val verifier = stored.getString("verifier", null) ?: return
        if (expected != id) return
        val exchanged = client.externalExchange(AccountExternalExchangeRequest(id, verifier, handoff))
        val session = exchanged.session ?: return
        val key = host.accountScope?.key ?: return
        host.coordinator.commitSession(session, key)
        stored.edit().clear().apply()
    }
}
