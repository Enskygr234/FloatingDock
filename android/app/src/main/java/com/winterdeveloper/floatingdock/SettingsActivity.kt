package com.winterdeveloper.floatingdock

import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.Settings
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.runtime.DisposableEffect

/** FloatingDock ayarlari: gorunum, dock, ada, izinler. */
class SettingsActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Prefs.init(this)
        enableEdgeToEdge()
        setContent { FdTheme { SettingsScreen { finish() } } }
    }

    override fun onResume() {
        super.onResume()
        NotifListener.instance?.refreshWindow()
    }
}

@Composable
fun SettingsScreen(onBack: () -> Unit) {
    val p = LocalPalette.current
    val ctx = LocalContext.current
    Prefs.version.intValue
    // izin ekranindan donunce durumlari tazele
    var resumed by remember { mutableIntStateOf(0) }
    val owner = LocalLifecycleOwner.current
    DisposableEffect(owner) {
        val o = LifecycleEventObserver { _, e -> if (e == Lifecycle.Event.ON_RESUME) resumed++ }
        owner.lifecycle.addObserver(o)
        onDispose { owner.lifecycle.removeObserver(o) }
    }
    resumed.let { }

    val notifOk = NotifListener.hasAccess(ctx)
    val overlayOk = Settings.canDrawOverlays(ctx)
    val isHome = ctx.packageManager.resolveActivity(Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME), 0)
        ?.activityInfo?.packageName == ctx.packageName

    Box(Modifier.fillMaxSize().background(if (p.dark) Color(0xFF0E1117) else Color(0xFFF3F4F7))) {
        Column(
            Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().verticalScroll(rememberScrollState()).padding(horizontal = 18.dp)
        ) {
            Row(Modifier.padding(top = 18.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Txt(L.t("Ayarlar", "Settings"), 26, p.text, FontWeight.SemiBold, Modifier.weight(1f))
                Txt(L.t("Kapat", "Close"), 15, p.accent, FontWeight.SemiBold, Modifier.clip(RoundedCornerShape(8.dp)).clickable { onBack() }.padding(8.dp))
            }

            // ---- ilk kurulum ----
            if (!isHome || !notifOk || (Prefs.island && !overlayOk)) {
                Group(L.t("Kurulum", "Setup"))
                Card {
                    if (!isHome) Action(L.t("Ana ekran uygulaması yap", "Set as home app"),
                        L.t("Ana ekran tuşu FloatingDock'u açsın", "The home button opens FloatingDock")) {
                        Apps.open(ctx, Intent(Settings.ACTION_HOME_SETTINGS))
                    }
                    if (!notifOk) Action(L.t("Bildirim erişimi ver", "Allow notification access"),
                        L.t("Ada, çalan müziği ve gelen bildirimleri gösterebilsin", "So the island can show music and notifications")) {
                        Apps.open(ctx, Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS))
                    }
                    if (Prefs.island && !overlayOk) Action(L.t("Diğer uygulamaların üzerinde göster", "Display over other apps"),
                        L.t("Ada her uygulamanın üstünde görünebilsin", "So the island can appear over every app")) {
                        Apps.open(ctx, Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + ctx.packageName)))
                    }
                }
            }

            // ---- gorunum ----
            Group(L.t("Görünüm", "Appearance"))
            Card {
                val langs = listOf("system", "tr", "en")
                Row2(L.t("Dil", "Language"), null) {
                    Segmented(listOf(L.t("Sistem", "System"), "Türkçe", "English"), langs.indexOf(Prefs.language).coerceAtLeast(0)) { Prefs.language = langs[it] }
                }
                val themes = listOf("system", "dark", "light")
                Row2(L.t("Tema", "Theme"), null) {
                    Segmented(listOf(L.t("Sistem", "System"), L.t("Koyu", "Dark"), L.t("Açık", "Light")), themes.indexOf(Prefs.theme).coerceAtLeast(0)) { Prefs.theme = themes[it] }
                }
                Row2(L.t("Vurgu rengi", "Accent color"), if (android.os.Build.VERSION.SDK_INT >= 31) L.t("Sistem: duvar kâğıdının rengi (Material You)", "System: your wallpaper color (Material You)") else null) {
                    Segmented(listOf(L.t("Buz mavisi", "Frost blue"), L.t("Sistem", "System")), if (Prefs.accent == "system") 1 else 0) { Prefs.accent = if (it == 1) "system" else "frost" }
                }
            }

            // ---- dock ----
            Group("Dock")
            Card {
                val sizes = listOf("small", "medium", "large")
                Row2(L.t("Boyut", "Size"), L.t("Uygulamaya basılı tutarak sabitle, taşı ya da kaldır", "Long-press an app to pin, move or remove it")) {
                    Segmented(listOf(L.t("Küçük", "Small"), L.t("Orta", "Medium"), L.t("Büyük", "Large")), sizes.indexOf(Prefs.dockSize).coerceAtLeast(0)) { Prefs.dockSize = sizes[it] }
                }
            }

            // ---- ada ----
            Group(L.t("Ada", "Island"))
            Card {
                Row2(L.t("Adayı göster", "Show the island"), L.t("Ekranın üstünde çalan müzik ve bildirimler", "Music and notifications at the top of the screen")) {
                    Toggle(Prefs.island) { Prefs.island = it; NotifListener.instance?.refreshWindow() }
                }
                Row2(L.t("Bildirimleri adada göster", "Show notifications in the island"), L.t("Sessiz bildirimler gösterilmez", "Silent notifications are skipped")) {
                    Toggle(Prefs.islandNotifications) { Prefs.islandNotifications = it }
                }
            }

            // ---- hakkinda ----
            Group(L.t("Hakkında", "About"))
            Card {
                val ver = try { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName } catch (_: Exception) { "" }
                Row2("FloatingDock " + ver, L.t("Masaüstündeki FloatingDock'un telefon sürümü · Winterdeveloper", "The phone version of FloatingDock for Windows · Winterdeveloper")) { }
                Action(L.t("GitHub sayfası", "GitHub page"), null) {
                    Apps.open(ctx, Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/Enskygr234/FloatingDock")))
                }
            }
            Spacer(Modifier.height(30.dp))
        }
    }
}

@Composable
private fun Group(t: String) {
    Txt(t, 13, LocalPalette.current.text3, FontWeight.SemiBold, Modifier.padding(start = 4.dp, top = 22.dp, bottom = 8.dp))
}

@Composable
private fun Card(content: @Composable () -> Unit) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxWidth().glass(p, RoundedCornerShape(18.dp), p.ink(0.05f)).padding(vertical = 4.dp)) { content() }
}

@Composable
private fun Row2(title: String, desc: String?, control: @Composable () -> Unit) {
    val p = LocalPalette.current
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.weight(1f).padding(end = 12.dp)) {
                Txt(title, 15, p.text, maxLines = 2)
                if (desc != null) Txt(desc, 12, p.text3, maxLines = 3)
            }
        }
        Spacer(Modifier.height(8.dp))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) { control() }
    }
}

@Composable
private fun Action(title: String, desc: String?, onClick: () -> Unit) {
    val p = LocalPalette.current
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 16.dp, vertical = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Txt(title, 15, p.accent, FontWeight.SemiBold)
            if (desc != null) Txt(desc, 12, p.text3, maxLines = 3)
        }
        Spacer(Modifier.width(8.dp))
        Txt("›", 22, p.text3)
    }
}

@Composable
private fun Toggle(on: Boolean, onChange: (Boolean) -> Unit) {
    val p = LocalPalette.current
    Switch(checked = on, onCheckedChange = onChange, colors = SwitchDefaults.colors(checkedTrackColor = p.accent, checkedThumbColor = p.onAccent))
}
