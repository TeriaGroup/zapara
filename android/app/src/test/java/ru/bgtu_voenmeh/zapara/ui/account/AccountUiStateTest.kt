package ru.bgtu_voenmeh.zapara.ui.account

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.resetMain
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.test.setMain
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSession
import ru.bgtu_voenmeh.zapara.data.accounts.AccountUser
import ru.bgtu_voenmeh.zapara.data.accounts.AccountVaultEntry
import ru.bgtu_voenmeh.zapara.data.accounts.MemoryAccountSessionVault
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import java.time.Instant

@OptIn(ExperimentalCoroutinesApi::class)
class AccountUiStateTest {
    private val dispatcher = StandardTestDispatcher()
    private val family = "11111111-1111-4111-8111-111111111111"
    private val device = "22222222-2222-4222-8222-222222222222"
    private val exportId = "33333333-3333-4333-8333-333333333333"
    private val txId = "44444444-4444-4444-8444-444444444444"
    private val other = "55555555-5555-4555-8555-555555555555"
    private val created = "2026-09-01T00:00:00Z"
    private val seen = "2026-09-08T12:00:00Z"
    private val expires = "2026-10-08T00:00:00Z"
    private val proof = opaque(7)

    @Before fun setMain() { Dispatchers.setMain(dispatcher) }
    @After fun reset() { Dispatchers.resetMain() }

    @Test
    fun guest_card_is_not_a_nav_section() {
        val guest = AccountUiState(ready = true, configured = true, guest = true, status = "Гостевой профиль: данные доступны без аккаунта и сети.")
        assertTrue(guest.guest)
        assertFalse(guest.busy)
        val account = guest.copy(guest = false, accountName = "Test.User")
        assertFalse(account.guest)
        assertTrue(account.accountName.isNotBlank())
    }

    @Test
    fun lifecycle_visibility_follows_capabilities_and_session() {
        val guest = AccountUiState(
            ready = true, configured = true, guest = true,
            vkAvailable = true, yandexAvailable = false, recoveryAvailable = true
        )
        assertTrue(guest.showVkLogin)
        assertFalse(guest.showYandexLogin)
        assertTrue(guest.showRecovery)
        assertFalse(guest.showDevices)
        assertFalse(guest.showExport)
        assertFalse(guest.showDelete)
        assertFalse(guest.showPasswordChange)
        assertFalse(guest.showIdentities)

        val noCaps = guest.copy(vkAvailable = false, recoveryAvailable = false)
        assertFalse(noCaps.showVkLogin)
        assertFalse(noCaps.showRecovery)

        val account = AccountUiState(
            ready = true, configured = true, guest = false, accountName = "Test.User",
            vkAvailable = true, yandexAvailable = false,
            identities = listOf(AccountIdentityRow("vk")),
            devices = listOf(AccountDeviceRow(family, device, "Pixel", "android", true))
        )
        assertTrue(account.showDevices)
        assertTrue(account.showExport)
        assertTrue(account.showDelete)
        assertTrue(account.showPasswordChange)
        assertTrue(account.showVkUnlink)
        assertFalse(account.showVkLink)
        assertFalse(account.showYandexLink)
        assertFalse(account.showRecovery)
        assertTrue(account.devices.single().current)

        val unconfigured = AccountUiState(ready = true, configured = false, vkAvailable = true, recoveryAvailable = true)
        assertFalse(unconfigured.showVkLogin)
        assertFalse(unconfigured.showRecovery)
        assertFalse(unconfigured.showDevices)
    }

    @Test
    fun reduce_updates_fields_and_confirm_delete_clears_secrets() {
        val start = AccountUiState(ready = true, configured = true, guest = false)
            .reduce(AccountEvent.CurrentPassword("password12ab"))
            .reduce(AccountEvent.NewPassword("password12cd"))
            .reduce(AccountEvent.Proof("proof-token"))
            .reduce(AccountEvent.RecoveryUsername("Test.User"))
        assertEquals("password12ab", start.currentPassword)
        assertEquals("password12cd", start.newPassword)
        assertEquals("proof-token", start.proof)
        val pending = start.reduce(AccountEvent.RequestDelete)
        assertTrue(pending.confirmDelete)
        val cancelled = pending.reduce(AccountEvent.CancelDelete)
        assertFalse(cancelled.confirmDelete)
        assertEquals("", cancelled.currentPassword)
        assertEquals("", cancelled.proof)
        val logged = start.reduce(AccountEvent.RequestLogout)
        assertTrue(logged.confirmLogout)
        assertEquals("", logged.password)
    }

    @Test
    fun native_pkce_is_s256_length_without_idp_hosts() {
        val pkce = nativePkce()
        assertEquals(43, pkce.challenge.length)
        assertEquals(43, pkce.verifier.length)
        assertTrue(pkce.challenge.all { it.isLetterOrDigit() || it == '-' || it == '_' })
        assertTrue(pkce.challenge != pkce.verifier)
        assertTrue(!pkce.challenge.contains("vk", ignoreCase = true))
        assertTrue(!pkce.verifier.contains("yandex", ignoreCase = true))
    }

    @Test
    fun vk_yandex_recovery_follow_capabilities_json() = runTest(dispatcher) {
        val http = FakeHttp {
            assertTrue(it.url.endsWith("api/v1/auth/capabilities"))
            assertTrue(!it.url.contains("id.vk") && !it.url.contains("oauth.yandex"))
            json("""{"password":true,"vk":true,"yandex":false,"registration":true,"recovery":true}""")
        }
        val caps = readUiCapabilities(http, "https://example.invalid/root/")
        assertTrue(caps.registration && caps.vk && caps.recovery)
        assertFalse(caps.yandex)
        assertEquals(1, http.requests.size)
        val shown = AccountUiState(ready = true, configured = true, guest = true).applyCaps(caps)
        assertTrue(shown.showVkLogin && shown.showRecovery)
        assertFalse(shown.showYandexLogin)
    }

    @Test
    fun load_devices_and_revoke_other_keeps_account() = runTest(dispatcher) {
        val access = testToken("za_", 1)
        val http = FakeHttp { call ->
            when (route(call)) {
                "GET auth/capabilities" -> capsJson()
                "GET account/identities" -> json("[]")
                "GET account/devices?limit=20" -> {
                    assertEquals("Bearer $access", call.headers["Authorization"])
                    json(devicesJson())
                }
                "DELETE account/devices/$other" -> HttpReply(204, ByteArray(0))
                else -> error(route(call))
            }
        }
        val vm = signedIn(http, access)
        advanceUntilIdle()
        vm.onEvent(AccountEvent.LoadDevices)
        advanceUntilIdle()
        assertEquals(2, vm.state.value.devices.size)
        vm.onEvent(AccountEvent.Revoke(other))
        advanceUntilIdle()
        assertFalse(vm.state.value.guest)
        assertEquals(family, vm.state.value.devices.single().familyId)
        assertTrue(http.requests.none { it.url.contains("id.vk") || it.url.contains("yandex") })
    }

    @Test
    fun change_password_clears_secrets_and_returns_to_guest() = runTest(dispatcher) {
        val http = FakeHttp { call ->
            when (route(call)) {
                "GET auth/capabilities" -> capsJson()
                "GET account/identities" -> json("[]")
                "POST account/password/change" -> {
                    assertEquals("""{"currentPassword":"password12ab","newPassword":"password12cd"}""", String(call.body!!))
                    HttpReply(204, ByteArray(0))
                }
                else -> error(route(call))
            }
        }
        val vm = signedIn(http, testToken("za_", 1))
        advanceUntilIdle()
        vm.onEvent(AccountEvent.CurrentPassword("password12ab"))
        vm.onEvent(AccountEvent.NewPassword("password12cd"))
        vm.onEvent(AccountEvent.ChangePassword)
        advanceUntilIdle()
        assertTrue(vm.state.value.guest)
        assertEquals("", vm.state.value.currentPassword)
        assertEquals("", vm.state.value.newPassword)
    }

    @Test
    fun export_uses_reauth_proof_and_keeps_download_local() = runTest(dispatcher) {
        val names = mutableListOf<String>()
        val http = FakeHttp { call ->
            when (route(call)) {
                "GET auth/capabilities" -> capsJson()
                "GET account/identities" -> json("[]")
                "POST account/reauthenticate" -> {
                    assertTrue(String(call.body!!).contains("\"purpose\":\"export\""))
                    json("""{"proofToken":"$proof","purpose":"export","expiresAt":"$seen"}""")
                }
                "POST account/exports" -> {
                    assertEquals("""{"proofToken":"$proof"}""", String(call.body!!))
                    json(exportJson(), 202)
                }
                "GET account/exports/$exportId/download" -> HttpReply(
                    200, """{"ok":true}""".toByteArray(), "application/json",
                    mapOf("Content-Disposition" to "attachment; filename=\"zapara-export-$exportId.json\"")
                )
                else -> error(route(call))
            }
        }
        val vm = signedIn(http, testToken("za_", 1), writeExport = { _, name -> names += name })
        advanceUntilIdle()
        vm.onEvent(AccountEvent.Proof("password12ab"))
        vm.onEvent(AccountEvent.CreateExport)
        advanceUntilIdle()
        assertTrue(vm.state.value.exportReady)
        assertEquals("", vm.state.value.proof)
        vm.onEvent(AccountEvent.DownloadExport)
        advanceUntilIdle()
        assertEquals("zapara-export-$exportId.json", names.single())
        assertFalse(vm.state.value.guest)
    }

    @Test
    fun delete_confirm_logs_out_without_wiping_guest_flag_path() = runTest(dispatcher) {
        val http = FakeHttp { call ->
            when (route(call)) {
                "GET auth/capabilities" -> capsJson()
                "GET account/identities" -> json("[]")
                "POST account/reauthenticate" -> json("""{"proofToken":"$proof","purpose":"delete_account","expiresAt":"$seen"}""")
                "DELETE account" -> {
                    assertEquals("""{"proofToken":"$proof"}""", String(call.body!!))
                    json("""{"status":"deleting","remoteWipe":false}""", 202)
                }
                else -> error(route(call))
            }
        }
        val vm = signedIn(http, testToken("za_", 1))
        advanceUntilIdle()
        vm.onEvent(AccountEvent.RequestDelete)
        assertTrue(vm.state.value.confirmDelete)
        vm.onEvent(AccountEvent.Proof("password12ab"))
        vm.onEvent(AccountEvent.ConfirmDelete)
        advanceUntilIdle()
        assertTrue(vm.state.value.guest)
        assertFalse(vm.state.value.confirmDelete)
        assertEquals("", vm.state.value.proof)
    }

    @Test
    fun recovery_request_has_no_bearer_and_vk_start_opens_mock_only() = runTest(dispatcher) {
        val opened = mutableListOf<String>()
        val http = FakeHttp { call ->
            when (route(call)) {
                "GET auth/capabilities" -> capsJson(vk = true, recovery = true)
                "POST auth/password-reset/request" -> {
                    assertEquals(null, call.headers["Authorization"])
                    assertEquals("""{"username":"Test.User"}""", String(call.body!!))
                    json("{}", 202)
                }
                "POST auth/password-reset/confirm" -> {
                    assertEquals("""{"token":"$proof","newPassword":"password12zz"}""", String(call.body!!))
                    HttpReply(204, ByteArray(0))
                }
                "POST auth/external/vk/start" -> {
                    val body = String(call.body!!)
                    assertTrue(body.contains("\"platform\":\"android\"") && body.contains("\"nativeChallengeMethod\":\"S256\""))
                    assertTrue(!body.contains("\"port\""))
                    assertTrue(!call.url.contains("id.vk") && !call.url.contains("oauth.yandex"))
                    json("""{"transactionId":"$txId","authorizeUrl":"https://example.invalid/mock/authorize","expiresAt":"$seen"}""")
                }
                else -> error(route(call))
            }
        }
        val vm = guestVm(http, openUrl = { opened += it })
        advanceUntilIdle()
        assertTrue(vm.state.value.showVkLogin && vm.state.value.showRecovery)
        vm.onEvent(AccountEvent.RecoveryUsername("Test.User"))
        vm.onEvent(AccountEvent.RequestReset)
        advanceUntilIdle()
        vm.onEvent(AccountEvent.Proof(proof))
        vm.onEvent(AccountEvent.NewPassword("password12zz"))
        vm.onEvent(AccountEvent.ConfirmReset)
        advanceUntilIdle()
        vm.onEvent(AccountEvent.StartVk)
        advanceUntilIdle()
        assertEquals(listOf("https://example.invalid/mock/authorize"), opened)
        assertTrue(opened.none { it.contains("id.vk") || it.contains("oauth.yandex") })
    }

    @Test
    fun unlink_identity_uses_proof_and_hides_row() = runTest(dispatcher) {
        val http = FakeHttp { call ->
            when (route(call)) {
                "GET auth/capabilities" -> capsJson(yandex = true)
                "GET account/identities" -> json("""[{"provider":"yandex","linkedAt":"$seen"}]""")
                "POST account/reauthenticate" -> json("""{"proofToken":"$proof","purpose":"unlink:yandex","expiresAt":"$seen"}""")
                "DELETE account/identities/yandex" -> {
                    assertEquals("""{"proofToken":"$proof"}""", String(call.body!!))
                    HttpReply(204, ByteArray(0))
                }
                else -> error(route(call))
            }
        }
        val vm = signedIn(http, testToken("za_", 1))
        advanceUntilIdle()
        assertTrue(vm.state.value.showYandexUnlink)
        vm.onEvent(AccountEvent.Proof("password12ab"))
        vm.onEvent(AccountEvent.Unlink("yandex"))
        advanceUntilIdle()
        assertTrue(vm.state.value.identities.none { it.provider == "yandex" })
        assertTrue(http.requests.none { it.url.contains("id.vk") || it.url.contains("oauth.yandex") })
    }

    private fun signedIn(
        http: FakeHttp,
        access: String,
        writeExport: (ByteArray, String) -> Unit = { _, _ -> }
    ): AccountViewModel {
        val vault = MemoryAccountSessionVault(scope().key)
        kotlinx.coroutines.runBlocking {
            vault.acquire().use { it.write(AccountVaultEntry.ready(scope().key, session(access))) }
        }
        var guest = false
        return AccountViewModel(
            AccountRuntime(
                client = AccountHttpClient(http, scope()),
                vault = vault,
                strings = ::copy,
                deviceId = { device },
                isGuest = { guest },
                commitSession = { _, _ -> true },
                logout = { _ ->
                    kotlinx.coroutines.runBlocking { vault.acquire().use { it.clear() } }
                    guest = true
                    true
                },
                openUrl = {},
                writeExport = writeExport,
                capabilitiesTransport = http,
                scopeBase = "https://example.invalid/root/",
                serverKey = scope().key
            )
        )
    }

    private fun guestVm(http: FakeHttp, openUrl: (String) -> Unit): AccountViewModel {
        return AccountViewModel(
            AccountRuntime(
                client = AccountHttpClient(http, scope()),
                vault = MemoryAccountSessionVault(scope().key),
                strings = ::copy,
                deviceId = { device },
                isGuest = { true },
                commitSession = { _, _ -> true },
                logout = { _ -> true },
                openUrl = openUrl,
                writeExport = { _, _ -> },
                capabilitiesTransport = http,
                scopeBase = "https://example.invalid/root/",
                serverKey = scope().key
            )
        )
    }

    private fun session(access: String) = AccountSession(
        user = AccountUser(family, "Test.User", null, Instant.parse(created)),
        familyId = family,
        accessToken = access,
        refreshToken = testToken("zr_", 2),
        tokenType = "Bearer",
        accessExpiresAt = Instant.parse(seen),
        refreshExpiresAt = Instant.parse(expires)
    )

    private fun scope() = AccountServerScope.parse("https://example.invalid/root")

    private fun route(call: HttpCall) = call.method + " " + call.url.substringAfter("/api/v1/")

    private fun json(body: String, status: Int = 200) = HttpReply(status, body.toByteArray(), "application/json")

    private fun capsJson(vk: Boolean = false, yandex: Boolean = false, recovery: Boolean = false) =
        json("""{"password":true,"vk":$vk,"yandex":$yandex,"registration":true,"recovery":$recovery}""")

    private fun devicesJson() = """
        {"devices":[
          {"familyId":"$family","deviceId":"$device","deviceName":"Pixel","platform":"android",
           "createdAt":"$created","lastSeenAt":"$seen","expiresAt":"$expires","isCurrent":true},
          {"familyId":"$other","deviceId":"66666666-6666-4666-8666-666666666666","deviceName":"Pad","platform":"android",
           "createdAt":"$created","lastSeenAt":"$seen","expiresAt":"$expires","isCurrent":false}
        ],"nextCursor":null}
    """.trimIndent()

    private fun exportJson() =
        """{"exportId":"$exportId","status":"ready","createdAt":"$seen","completedAt":"$seen","expiresAt":"$expires"}"""

    private fun opaque(fill: Int): String =
        java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(32) { fill.toByte() })

    private fun copy(id: Int): String = when (id) {
        R.string.account_unconfigured -> "Сервер аккаунтов не настроен"
        R.string.account_guest -> "Гостевой профиль: данные доступны без аккаунта и сети."
        R.string.account_local -> "Локальные данные аккаунта. Синхронизация личных данных пока недоступна."
        R.string.account_failed -> "Операция аккаунта не выполнена. Повторите попытку позже."
        R.string.account_logout_local -> "Вы вышли на этом устройстве. Отзыв сессии на сервере не подтверждён этой операцией интерфейса."
        R.string.account_transition_failed -> "Профиль не переключён. Завершите текущие операции и повторите попытку."
        R.string.account_reauth -> "Требуется повторный вход. Локальные данные сохранены."
        R.string.account_validation -> "Логин: 3–32 латинские буквы, цифры, точка, дефис или подчёркивание. Пароль: 12–128 символов. Имя: до 80 символов."
        R.string.account_export_download -> "Скачать экспорт"
        else -> "id$id"
    }
}
