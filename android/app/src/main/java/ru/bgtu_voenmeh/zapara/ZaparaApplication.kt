package ru.bgtu_voenmeh.zapara

import android.app.Application
import androidx.lifecycle.ViewModelStore
import androidx.lifecycle.ViewModelStoreOwner
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import ru.bgtu_voenmeh.zapara.data.HomeworkService
import ru.bgtu_voenmeh.zapara.data.OverrideService
import ru.bgtu_voenmeh.zapara.data.SchedCtx
import ru.bgtu_voenmeh.zapara.data.ScheduleRepository
import ru.bgtu_voenmeh.zapara.data.accounts.AccountHttpClient
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountSessionVault
import ru.bgtu_voenmeh.zapara.data.accounts.KeystoreAccountSessionVault
import ru.bgtu_voenmeh.zapara.data.accounts.MemoryAccountSessionVault
import ru.bgtu_voenmeh.zapara.data.api.ApiRefreshCoordinator
import ru.bgtu_voenmeh.zapara.data.api.RoomTimetableStore
import ru.bgtu_voenmeh.zapara.data.api.TimetableSource
import ru.bgtu_voenmeh.zapara.data.api.UrlConnectionTransport
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHttpClient
import ru.bgtu_voenmeh.zapara.data.sync.PrivateSyncCoordinator
import ru.bgtu_voenmeh.zapara.data.sync.PrivateSyncHttpClient
import ru.bgtu_voenmeh.zapara.data.sync.RoomSyncOutbox
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileCoordinator
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileGraph
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileRestore
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork
import kotlinx.coroutines.runBlocking
import ru.bgtu_voenmeh.zapara.data.LecturerStore
import ru.bgtu_voenmeh.zapara.data.MapStore
import java.time.LocalDateTime

class ZaparaApplication : Application() {
    lateinit var host: AndroidProfileHost
        private set
    val container: AppContainer get() = host.container

    override fun onCreate() {
        super.onCreate()
        host = AndroidProfileHost(this)
    }
}

class AndroidProfileHost(val app: Application) : ViewModelStoreOwner {
    private val store = ViewModelStore()
    override val viewModelStore: ViewModelStore get() = store
    private val apiBase: String? = BuildConfig.API_BASE_URL.ifBlank { null }
    private val transport = UrlConnectionTransport()
    val accountScope: AccountServerScope? = try {
        apiBase?.let { AccountServerScope.parse(it) }
    } catch (_: Exception) {
        null
    }
    val vault: AccountSessionVault = accountScope?.let { KeystoreAccountSessionVault(app, it.key) }
        ?: MemoryAccountSessionVault("0".repeat(64))
    val accounts: AccountHttpClient? = accountScope?.let { AccountHttpClient(transport, it) }
    private val containers = HashMap<String, AppContainer>()
    val generation = MutableStateFlow(0L)
    val coordinator: ProfileCoordinator
    var container: AppContainer
        private set

    init {
        val start = runBlocking { ProfileRestore.descriptor(vault) }
        val opened = open(start)
        container = opened
        ScheduleRepository.attach(opened.repo)
        coordinator = ProfileCoordinator(
            ProfileGraph(opened.profile, opened.repo.store, opened.work) { opened.close() },
            open = { desc ->
                val next = open(desc)
                ProfileGraph(desc, next.repo.store, next.work) { next.close() }
            },
            vault,
            onPublished = { publish(it) }
        )
    }

    fun open(descriptor: ProfileDescriptor): AppContainer {
        containers[descriptor.databaseName]?.let { existing ->
            if (!existing.closed) {
                existing.attachPrivateSync()
                return existing
            }
        }
        val db = ScheduleRepository.openDatabase(app, descriptor)
        val store = RoomTimetableStore(db)
        val work = ProfileWork()
        val repo = ScheduleRepository(db, store, work)
        val api = ApiRefreshCoordinator(store, work, apiBase, transport)
        val created = AppContainer(
            app, descriptor, db, repo, work, api,
            communities = accountScope?.let { CommunityHttpClient(transport, it) },
            readAccessToken = { readAccessToken() },
            syncHttp = if (descriptor.isGuest) null else accountScope?.let { PrivateSyncHttpClient(transport, it.baseUri) }
        )
        created.repo.outbox = created.outbox
        created.attachPrivateSync()
        containers[descriptor.databaseName] = created
        return created
    }

    private suspend fun readAccessToken(): String? = try {
        vault.acquire().use { it.read()?.session?.accessToken }
    } catch (e: Exception) {
        android.util.Log.w("ZaparaProfile", "token", e)
        null
    }

    suspend fun restore() {
        val entry = try {
            vault.acquire().use { it.read() }
        } catch (e: Exception) {
            android.util.Log.w("ZaparaProfile", "vault restore", e)
            return
        } ?: return
        try {
            coordinator.commitSession(entry.session, entry.serverKey)
        } catch (e: Exception) {
            android.util.Log.w("ZaparaProfile", "profile restore", e)
        }
    }

    fun publish(graph: ProfileGraph) {
        val next = containers[graph.descriptor.databaseName] ?: open(graph.descriptor)
        if (container !== next) {
            ScheduleRepository.detach(container.repo)
            store.clear()
            container = next
            ScheduleRepository.attach(next.repo)
            generation.value = generation.value + 1
        }
    }
}

class AppContainer(
    val app: Application,
    val profile: ProfileDescriptor,
    val db: ru.bgtu_voenmeh.zapara.data.db.ZaparaDatabase,
    val repo: ScheduleRepository,
    val work: ru.bgtu_voenmeh.zapara.data.profiles.ProfileWork,
    val api: ApiRefreshCoordinator,
    val communities: CommunityHttpClient? = null,
    private val readAccessToken: (suspend () -> String?)? = null,
    private val syncHttp: PrivateSyncHttpClient? = null
) {
    val timetable = TimetableSource(api, repo.store, { repo.refresh() }) { repo.applyBundled(app) }
    var closed: Boolean = false
        private set
    val outbox = RoomSyncOutbox.from(db, enabled = !profile.isGuest)
    val privateSync: PrivateSyncCoordinator? =
        if (profile.isGuest) null else PrivateSyncCoordinator(outbox, work)
    val mapStore by lazy { MapStore(app) }
    val lecturerStore by lazy { LecturerStore(app) }
    val overrides by lazy { OverrideService(db.overrideDao(), outbox) }
    val homework by lazy {
        HomeworkService(
            db.homeworkDao(),
            lessonsFor = { gid, dow, parity ->
                repo.allForGroup(gid).filter { it.dayOfWeek == dow && (it.parity == parity || it.parity == 0) }
            },
            ctx = { repo.settings().let { SchedCtx(it.myGroupId.orEmpty(), it.periodStart, it.weekCount, it.parityInvert) } },
            outbox = outbox
        )
    }

    suspend fun accessToken(): String? = readAccessToken?.invoke()

    fun attachPrivateSync() {
        val http = syncHttp ?: return
        val token = readAccessToken ?: return
        privateSync?.attach(http, token)
    }
    val clock: () -> LocalDateTime = { LocalDateTime.now() }
    val copy: ru.bgtu_voenmeh.zapara.ui.UiCopy by lazy { ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy(app) }
    val events = ru.bgtu_voenmeh.zapara.ui.AppEvents()
    val toasts = ru.bgtu_voenmeh.zapara.ui.components.ToastCenter()
    val update by lazy {
        ru.bgtu_voenmeh.zapara.ui.settings.UpdateViewModel(
            source = ru.bgtu_voenmeh.zapara.ui.settings.GitHubUpdateSource(app),
            autoEnabled = { ru.bgtu_voenmeh.zapara.data.AutoUpdate.isAutoUpdateEnabled(app) },
            isNewer = { latest, current -> ru.bgtu_voenmeh.zapara.data.AutoUpdate.isNewer(latest, current) },
            currentTag = ru.bgtu_voenmeh.zapara.data.AutoUpdate.CURRENT_TAG,
            copy = copy
        )
    }

    fun notifyDataChanged() {
        events.emit(ru.bgtu_voenmeh.zapara.ui.AppEvent.ScheduleChanged)
        events.emit(ru.bgtu_voenmeh.zapara.ui.AppEvent.PersonalizationChanged)
        events.emit(ru.bgtu_voenmeh.zapara.ui.AppEvent.GroupChanged)
    }

    fun close() {
        if (closed) return
        closed = true
        privateSync?.close()
        api.stop()
        runCatching { db.close() }
    }
}
