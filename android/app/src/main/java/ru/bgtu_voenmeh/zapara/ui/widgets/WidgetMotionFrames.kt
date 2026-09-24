package ru.bgtu_voenmeh.zapara.ui.widgets

import android.appwidget.AppWidgetManager
import android.content.ComponentName
import android.content.Context
import android.graphics.Bitmap
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.os.SystemClock
import android.provider.Settings
import android.util.Log
import android.view.View
import android.widget.RemoteViews
import ru.bgtu_voenmeh.zapara.ZaparaApplication

/** All entry points run on main, together with widget publication and profile preparation. */
object WidgetMotionPlayer {
    private val main = Handler(Looper.getMainLooper())
    private val tokens = WidgetMotionTokens()
    private data class FinalFace(
        val context: Context,
        val layoutId: Int,
        val overlayId: Int?,
        val identity: WidgetJobIdentity,
        val views: RemoteViews,
        val mayAnimate: Boolean
    )
    private data class Playback(val token: Long, var face: FinalFace, val callbacks: MutableList<Runnable>)
    private val finals = mutableMapOf<Int, FinalFace>()
    private val playing = mutableMapOf<Int, Playback>()
    private val policies = WidgetMotionPolicies()

    fun beginPolicyObservation(): Long = policies.beginObservation()

    fun updatePolicy(identity: WidgetJobIdentity, policy: WidgetMotionPolicy, revision: Long) {
        requireMain()
        if (policies.update(identity, policy, revision) && !policy.enabled) cancelAll()
    }

    fun beginPreferenceChange(identity: WidgetJobIdentity): Long {
        requireMain()
        val revision = policies.beginChange(identity)
        cancelAll()
        return revision
    }

    fun finishPreferenceChange(identity: WidgetJobIdentity, revision: Long) {
        requireMain()
        policies.finishChange(identity, revision)
        cancelAll()
    }

    /** Publish accessible final content first. A first face or a cleared face cannot animate. */
    fun publishFinal(
        context: Context, widgetId: Int, layoutId: Int, overlayId: Int?,
        identity: WidgetJobIdentity, policy: WidgetMotionPolicy, views: RemoteViews, cleared: Boolean = false
    ) {
        requireMain()
        prepare(context, policy)
        val previous = finals[widgetId]
        cancel(widgetId)
        if (!WidgetJobs.accept(identity, currentIdentity(context))) return
        overlayId?.let {
            views.setViewVisibility(it, View.GONE)
            views.setImageViewBitmap(it, null)
        }
        AppWidgetManager.getInstance(context).updateAppWidget(widgetId, views)
        if (cleared) finals.remove(widgetId) else finals[widgetId] = FinalFace(
            context.applicationContext, layoutId, overlayId, identity, views,
            policy.enabled && previous?.identity == identity && previous.layoutId == layoutId
        )
    }

    /** Called even for timer-only partial repaints, so removal and reduced motion also prune work. */
    fun prepare(context: Context, policy: WidgetMotionPolicy): Set<Int> {
        requireMain()
        if (!policy.enabled) cancelAll()
        return prune(context)
    }

    fun prune(context: Context): Set<Int> {
        requireMain()
        val ids = placedIds(context)
        playing.keys.toList().filter { it !in ids }.forEach { cancel(it, hide = false) }
        finals.keys.retainAll(ids)
        tokens.retainIds(ids)
        return ids
    }

    fun play(
        context: Context, widgetId: Int, layoutId: Int, overlayId: Int,
        identity: WidgetJobIdentity, policy: WidgetMotionPolicy, bitmapAt: (Float) -> Bitmap
    ) {
        requireMain()
        cancel(widgetId)
        val face = finals[widgetId] ?: return
        if (!policy.enabled || !face.mayAnimate || face.identity != identity ||
            face.layoutId != layoutId || face.overlayId != overlayId ||
            !WidgetJobs.accept(identity, currentIdentity(context)) || !motionAllowed(context, identity)) return
        val token = tokens.next(widgetId)
        val playback = Playback(token, face, mutableListOf())
        playing[widgetId] = playback
        val started = SystemClock.uptimeMillis()
        policy.frames().forEachIndexed { index, frame ->
            val callback = Runnable {
                if (!tokens.mayDraw(widgetId, token, identity, currentIdentity(context)) || !motionAllowed(context, identity)) {
                    if (playing[widgetId] === playback) cancel(widgetId)
                    return@Runnable
                }
                try {
                    // A provider removal may arrive between the original publication and this frame.
                    if (AppWidgetManager.getInstance(context).getAppWidgetInfo(widgetId) == null) {
                        cancel(widgetId, hide = false)
                        finals.remove(widgetId)
                        return@Runnable
                    }
                    val bitmap = bitmapAt(frame.progress)
                    require(bitmap.config == Bitmap.Config.ARGB_8888 && maxOf(bitmap.width, bitmap.height) <= 640) {
                        "Widget frames must be ARGB_8888 and at most 640 px"
                    }
                    // Rendering may itself observe a profile switch or replacement; recheck at the boundary.
                    if (!tokens.mayDraw(widgetId, token, identity, currentIdentity(context)) || !motionAllowed(context, identity)) {
                        if (playing[widgetId] === playback) cancel(widgetId)
                        return@Runnable
                    }
                    val partial = RemoteViews(context.packageName, layoutId).apply {
                        setImageViewBitmap(overlayId, bitmap)
                        setViewVisibility(overlayId, View.VISIBLE)
                    }
                    AppWidgetManager.getInstance(context).partiallyUpdateAppWidget(widgetId, partial)
                    if (index == policy.frameCount - 1) cancel(widgetId)
                } catch (error: Exception) {
                    Log.w("ZaparaWidget", "motion frame", error)
                    cancel(widgetId)
                } catch (error: OutOfMemoryError) {
                    Log.w("ZaparaWidget", "motion allocation", error)
                    cancel(widgetId)
                }
            }
            playback.callbacks += callback
            main.postAtTime(callback, started + frame.delayMs)
        }
    }

    fun cancel(widgetId: Int) { requireMain(); cancel(widgetId, hide = true) }
    fun isRunning(widgetId: Int): Boolean { requireMain(); return widgetId in playing }

    /** A clock pulse refreshes cleanup content without interrupting the current ring frame. */
    fun refreshFinal(widgetId: Int, identity: WidgetJobIdentity, views: RemoteViews): Boolean {
        requireMain()
        val face = finals[widgetId] ?: return false
        if (face.identity != identity || !WidgetJobs.accept(identity, currentIdentity(face.context))) return false
        face.overlayId?.let {
            views.setViewVisibility(it, View.GONE)
            views.setImageViewBitmap(it, null)
        }
        val updated = face.copy(views = views)
        finals[widgetId] = updated
        playing[widgetId]?.face = updated
        return true
    }

    fun cancelAll() {
        requireMain()
        playing.keys.toList().forEach { cancel(it) }
    }

    fun clear() {
        cancelAll()
        finals.clear()
        tokens.retainIds(emptySet())
    }

    private fun cancel(widgetId: Int, hide: Boolean) {
        val playback = playing.remove(widgetId) ?: return
        tokens.next(widgetId)
        playback.callbacks.forEach(main::removeCallbacks)
        if (hide) hideOverlay(widgetId, playback.face)
    }

    private fun hideOverlay(widgetId: Int, face: FinalFace) {
        val overlay = face.overlayId ?: return
        try {
            val manager = AppWidgetManager.getInstance(face.context)
            if (WidgetJobs.accept(face.identity, currentIdentity(face.context))) {
                // Replace the accumulated partial bitmap cache with the already-published final face.
                manager.updateAppWidget(widgetId, face.views)
            } else {
                // Never restore a prior profile's text while its privacy clear is pending.
                val hidden = RemoteViews(face.context.packageName, face.layoutId).apply {
                    setViewVisibility(overlay, View.GONE)
                    setImageViewBitmap(overlay, null)
                }
                manager.partiallyUpdateAppWidget(widgetId, hidden)
            }
        } catch (error: Exception) {
            Log.w("ZaparaWidget", "motion hide", error)
            try {
                val hidden = RemoteViews(face.context.packageName, face.layoutId).apply {
                    setViewVisibility(overlay, View.GONE)
                    setImageViewBitmap(overlay, null)
                }
                AppWidgetManager.getInstance(face.context).partiallyUpdateAppWidget(widgetId, hidden)
            } catch (retry: Exception) { Log.w("ZaparaWidget", "motion hide retry", retry) }
        }
    }

    private fun currentIdentity(context: Context): WidgetJobIdentity {
        val host = (context.applicationContext as ZaparaApplication).host
        return WidgetJobIdentity.of(host.container.profile, host.generation.value)
    }

    private fun motionAllowed(context: Context, identity: WidgetJobIdentity): Boolean {
        if (!policies.allows(identity)) return false
        // Snapshot timing remains immutable. These live platform gates prevent an already queued
        // enabled snapshot from restarting motion after screen-off or a system preference change.
        return try {
            (context.getSystemService(Context.POWER_SERVICE) as PowerManager).isInteractive &&
                Settings.Global.getFloat(context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f) > 0f
        } catch (_: Exception) { false }
    }

    private fun placedIds(context: Context): Set<Int> {
        val manager = AppWidgetManager.getInstance(context)
        return listOf(ScheduleWidgetProvider::class.java, HomeworkWidgetProvider::class.java,
            TimerWidgetProvider::class.java, WayfinderWidgetProvider::class.java, WeekWidgetProvider::class.java)
            .flatMap { manager.getAppWidgetIds(ComponentName(context, it)).toList() }.toSet()
    }

    private fun requireMain() { check(Looper.myLooper() == Looper.getMainLooper()) }
}
