package com.winterdeveloper.floatingdock

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shadow
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import java.text.SimpleDateFormat
import java.util.Date

/** Ana ekran (launcher): saat, havada duran dock, uygulama menusu. */
class HomeActivity : ComponentActivity() {
    /** Uygulama menusu acik mi? Ana ekran tusuna basinca kapanir. */
    private val drawerOpen = mutableStateOf(false)

    private val packagesChanged = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) { lifecycleScope.launch { Apps.load(this@HomeActivity) } }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(this)
        enableEdgeToEdge()
        lifecycleScope.launch { Apps.load(this@HomeActivity) }
        val f = IntentFilter().apply {
            addAction(Intent.ACTION_PACKAGE_ADDED); addAction(Intent.ACTION_PACKAGE_REMOVED); addAction(Intent.ACTION_PACKAGE_CHANGED)
            addDataScheme("package")
        }
        ContextCompat.registerReceiver(this, packagesChanged, f, ContextCompat.RECEIVER_EXPORTED)
        setContent { FdTheme { Home(drawerOpen) } }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        drawerOpen.value = false // ana ekran tusu
    }

    override fun onResume() {
        super.onResume()
        NotifListener.instance?.refreshWindow() // izin ayarlardan yeni verildiyse ada acilsin
    }

    override fun onDestroy() {
        try { unregisterReceiver(packagesChanged) } catch (_: Exception) { }
        super.onDestroy()
    }
}

@Composable
fun Home(drawerOpen: androidx.compose.runtime.MutableState<Boolean>) {
    val ctx = LocalContext.current
    Prefs.version.intValue
    BackHandler(enabled = drawerOpen.value) { drawerOpen.value = false }

    Box(
        Modifier.fillMaxSize()
            // bos alanda yukari kaydir: uygulama menusu; basili tut: ayarlar
            .pointerInput(Unit) {
                var total = 0f
                detectVerticalDragGestures(onDragStart = { total = 0f }, onDragEnd = { if (total < -80) drawerOpen.value = true }) { _, dy -> total += dy }
            }
            .pointerInput(Unit) {
                detectTapGestures(onLongPress = { ctx.startActivity(Intent(ctx, SettingsActivity::class.java)) })
            }
    ) {
        Clock(Modifier.align(Alignment.TopStart).statusBarsPadding().padding(start = 26.dp, top = 40.dp))
        Dock(Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(bottom = 14.dp)) { drawerOpen.value = true }

        AnimatedVisibility(
            visible = drawerOpen.value,
            enter = fadeIn(tween(160)) + slideInVertically(tween(260)) { it / 6 },
            exit = fadeOut(tween(140)) + slideOutVertically(tween(180)) { it / 8 }
        ) {
            Drawer(onClose = { drawerOpen.value = false })
        }
    }
}

/** Buyuk saat, tarih ve selamlama (duvar kagidi uzerinde okunabilsin diye hafif golgeli). */
@Composable
fun Clock(modifier: Modifier) {
    var now by remember { mutableStateOf(Date()) }
    LaunchedEffect(Unit) {
        while (true) { now = Date(); delay(1000L - System.currentTimeMillis() % 1000L) }
    }
    val shadow = Shadow(Color.Black.copy(alpha = 0.45f), blurRadius = 12f)
    val loc = L.locale
    Column(modifier) {
        Text(SimpleDateFormat("HH:mm", loc).format(now), color = Color.White, fontSize = 64.sp, fontWeight = FontWeight.Light,
            style = TextStyle(shadow = shadow))
        Text(L.cap(SimpleDateFormat(if (L.en) "EEEE, MMMM d" else "d MMMM EEEE", loc).format(now)), color = Color.White.copy(alpha = 0.9f),
            fontSize = 17.sp, style = TextStyle(shadow = shadow))
        Spacer(Modifier.height(4.dp))
        Text(greeting(), color = Color.White.copy(alpha = 0.75f), fontSize = 15.sp, style = TextStyle(shadow = shadow))
    }
}

fun greeting(): String {
    val h = java.util.Calendar.getInstance().get(java.util.Calendar.HOUR_OF_DAY)
    return when (h) {
        in 5..11 -> L.t("Günaydın", "Good morning")
        in 12..17 -> L.t("İyi günler", "Good afternoon")
        in 18..21 -> L.t("İyi akşamlar", "Good evening")
        else -> L.t("İyi geceler", "Good night")
    }
}

/** Havada duran cam dock: Baslat dugmesi + sabitlenen uygulamalar. */
@Composable
fun Dock(modifier: Modifier, openDrawer: () -> Unit) {
    val p = LocalPalette.current
    val apps by Apps.all
    val pins = (Prefs.pins ?: emptyList()).mapNotNull { k -> apps.firstOrNull { it.key == k } }
    val icon = Prefs.iconDp
    val shape = RoundedCornerShape(26.dp)
    Row(
        modifier.glass(p, shape, p.dock).padding(horizontal = 10.dp, vertical = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Box(Modifier.size(icon.dp).pressable(openDrawer), contentAlignment = Alignment.Center) { StartGlyph((icon * 0.78f).dp) }
        if (pins.isNotEmpty()) Box(Modifier.width(1.dp).height((icon * 0.55f).dp).background(p.stroke))
        for (a in pins) DockIcon(a, icon)
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun DockIcon(a: AppInfo, size: Int) {
    val ctx = LocalContext.current
    var menu by remember { mutableStateOf(false) }
    Box {
        Image(
            a.icon, a.label,
            Modifier.size(size.dp).clip(RoundedCornerShape(size.dp * 0.28f))
                .combinedClickable(interactionSource = remember { MutableInteractionSource() }, indication = null,
                    onLongClick = { menu = true }, onClick = { Apps.launch(ctx, a) })
        )
        AppMenu(a, menu, inDock = true) { menu = false }
    }
}

/** Uygulamaya basili tutunca: sabitle / kaldir, tasima, uygulama bilgisi, kaldirma. */
@Composable
fun AppMenu(a: AppInfo, open: Boolean, inDock: Boolean, onDismiss: () -> Unit) {
    val ctx = LocalContext.current
    DropdownMenu(expanded = open, onDismissRequest = onDismiss) {
        Text(a.label, Modifier.padding(horizontal = 16.dp, vertical = 6.dp), fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
        val pinned = Apps.isPinned(a.key)
        DropdownMenuItem(text = { Text(if (pinned) L.t("Dock'tan kaldır", "Unpin from dock") else L.t("Dock'a sabitle", "Pin to dock")) },
            onClick = { Apps.togglePin(a.key); onDismiss() })
        if (inDock) {
            DropdownMenuItem(text = { Text(L.t("Sola taşı", "Move left")) }, onClick = { Apps.move(a.key, -1); onDismiss() })
            DropdownMenuItem(text = { Text(L.t("Sağa taşı", "Move right")) }, onClick = { Apps.move(a.key, 1); onDismiss() })
        }
        DropdownMenuItem(text = { Text(L.t("Uygulama bilgisi", "App info")) }, onClick = { Apps.appInfo(ctx, a); onDismiss() })
        DropdownMenuItem(text = { Text(L.t("Kaldır", "Uninstall")) }, onClick = { Apps.uninstall(ctx, a); onDismiss() })
    }
}
