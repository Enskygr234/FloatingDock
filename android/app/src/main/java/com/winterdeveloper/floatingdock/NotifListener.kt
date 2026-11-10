package com.winterdeveloper.floatingdock

import android.app.Notification
import android.app.NotificationManager
import android.content.ComponentName
import android.content.Context
import android.graphics.PixelFormat
import android.media.MediaMetadata
import android.media.session.MediaController
import android.media.session.MediaSessionManager
import android.media.session.PlaybackState
import android.os.Build
import android.provider.Settings
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.view.Gravity
import android.view.WindowManager
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.ComposeView
import androidx.core.graphics.drawable.toBitmap
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.setViewTreeLifecycleOwner
import androidx.savedstate.SavedStateRegistry
import androidx.savedstate.SavedStateRegistryController
import androidx.savedstate.SavedStateRegistryOwner
import androidx.savedstate.setViewTreeSavedStateRegistryOwner

/**
 * Bildirim dinleyicisi: gelen bildirimleri ve calan muzigi adaya iletir.
 * Ada penceresi de burada yasar, cunku sistem bu servisi surekli acik tutar.
 */
class NotifListener : NotificationListenerService(), LifecycleOwner, SavedStateRegistryOwner {
    companion object {
        var instance: NotifListener? = null
            private set

        /** Bildirim erisimi verilmis mi? */
        fun hasAccess(c: Context): Boolean {
            val flat = Settings.Secure.getString(c.contentResolver, "enabled_notification_listeners") ?: return false
            return flat.split(':').any { ComponentName.unflattenFromString(it)?.packageName == c.packageName }
        }
    }

    private val lifecycleRegistry = LifecycleRegistry(this)
    private val savedState = SavedStateRegistryController.create(this)
    override val lifecycle: Lifecycle get() = lifecycleRegistry
    override val savedStateRegistry: SavedStateRegistry get() = savedState.savedStateRegistry

    private var view: ComposeView? = null
    private var connected = false
    private var sessions: MediaSessionManager? = null
    private var current: MediaController? = null
    private val me by lazy { ComponentName(this, NotifListener::class.java) }

    private val sessionsChanged = MediaSessionManager.OnActiveSessionsChangedListener { pick(it) }
    private val mediaCallback = object : MediaController.Callback() {
        override fun onPlaybackStateChanged(state: PlaybackState?) { publish() }
        override fun onMetadataChanged(metadata: MediaMetadata?) { publish() }
        override fun onSessionDestroyed() {
            current = null
            pick(try { sessions?.getActiveSessions(me) } catch (_: SecurityException) { null })
        }
    }

    override fun onCreate() {
        super.onCreate()
        Prefs.init(this)
        savedState.performRestore(null)
        lifecycleRegistry.currentState = Lifecycle.State.CREATED
        instance = this
    }

    override fun onListenerConnected() {
        connected = true
        lifecycleRegistry.currentState = Lifecycle.State.RESUMED
        try {
            val msm = getSystemService(MediaSessionManager::class.java)
            sessions = msm
            msm.addOnActiveSessionsChangedListener(sessionsChanged, me)
            pick(msm.getActiveSessions(me))
        } catch (_: SecurityException) { }
        refreshWindow()
    }

    override fun onListenerDisconnected() {
        connected = false
        try { sessions?.removeOnActiveSessionsChangedListener(sessionsChanged) } catch (_: Exception) { }
        removeWindow()
    }

    override fun onDestroy() {
        removeWindow()
        current?.unregisterCallback(mediaCallback)
        lifecycleRegistry.currentState = Lifecycle.State.DESTROYED
        if (instance === this) instance = null
        super.onDestroy()
    }

    // ---------------- ada penceresi ----------------

    /** Ayar ya da izin degisince cagrilir: adayi ac / kapat. */
    fun refreshWindow() {
        val want = connected && Prefs.island && Settings.canDrawOverlays(this)
        if (want && view == null) addWindow() else if (!want && view != null) removeWindow()
    }

    private fun addWindow() {
        try {
            val wm = getSystemService(WindowManager::class.java)
            val lp = WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT
            )
            lp.gravity = Gravity.TOP or Gravity.CENTER_HORIZONTAL
            // durum cubugunun hemen altinda (uygulama ustu pencereler durum cubugunun ustune cikamaz)
            lp.y = statusBarHeight() + (2 * resources.displayMetrics.density).toInt()
            if (Build.VERSION.SDK_INT >= 28) lp.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES
            val v = ComposeView(this)
            v.setViewTreeLifecycleOwner(this)
            v.setViewTreeSavedStateRegistryOwner(this)
            v.setContent { FdTheme { Island() } }
            wm.addView(v, lp)
            view = v
        } catch (_: Exception) {
            view = null
        }
    }

    private fun removeWindow() {
        val v = view ?: return
        view = null
        try { getSystemService(WindowManager::class.java).removeView(v) } catch (_: Exception) { }
    }

    private fun statusBarHeight(): Int {
        val id = resources.getIdentifier("status_bar_height", "dimen", "android")
        return if (id > 0) resources.getDimensionPixelSize(id) else 0
    }

    // ---------------- muzik ----------------

    private fun pick(list: List<MediaController>?) {
        val next = list?.firstOrNull { it.playbackState?.state == PlaybackState.STATE_PLAYING } ?: list?.firstOrNull()
        if (next?.sessionToken != current?.sessionToken) {
            current?.unregisterCallback(mediaCallback)
            current = next
            next?.registerCallback(mediaCallback)
        }
        publish()
    }

    private fun publish() {
        val c = current
        IslandState.controller = c
        val md = c?.metadata
        val title = md?.getString(MediaMetadata.METADATA_KEY_TITLE) ?: md?.description?.title?.toString()
        if (c == null || title.isNullOrBlank()) { IslandState.setMedia(null); return }
        val artist = md?.getString(MediaMetadata.METADATA_KEY_ARTIST) ?: md?.description?.subtitle?.toString() ?: ""
        val art = md?.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART) ?: md?.getBitmap(MediaMetadata.METADATA_KEY_ART) ?: md?.description?.iconBitmap
        val playing = c.playbackState?.state == PlaybackState.STATE_PLAYING
        IslandState.setMedia(MediaNow(title, artist, art?.asImageBitmap(), playing, appLabel(c.packageName)))
    }

    // ---------------- bildirimler ----------------

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        if (!Prefs.islandNotifications || sbn.packageName == packageName) return
        val n = sbn.notification
        if (sbn.isOngoing || (n.flags and Notification.FLAG_GROUP_SUMMARY) != 0) return
        if (n.extras.containsKey(Notification.EXTRA_MEDIA_SESSION)) return
        // sessiz bildirimler ve rahatsiz etme modunda engellenenler gosterilmez
        try {
            val r = Ranking()
            if (currentRanking.getRanking(sbn.key, r)) {
                if (r.importance < NotificationManager.IMPORTANCE_DEFAULT || !r.matchesInterruptionFilter()) return
            }
        } catch (_: Exception) { }
        val title = n.extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()?.trim()
        if (title.isNullOrEmpty()) return
        val text = (n.extras.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: n.extras.getCharSequence(Notification.EXTRA_TEXT))?.toString()?.trim() ?: ""
        val icon = try { packageManager.getApplicationIcon(sbn.packageName).toBitmap(96, 96).asImageBitmap() } catch (_: Exception) { null }
        IslandState.show(Note(appLabel(sbn.packageName), title, text.replace('\n', ' '), icon, n.contentIntent))
    }

    private fun appLabel(pkg: String): String = try {
        packageManager.getApplicationLabel(packageManager.getApplicationInfo(pkg, 0)).toString()
    } catch (_: Exception) { pkg }
}
