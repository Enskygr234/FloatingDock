package com.winterdeveloper.floatingdock

import android.content.ActivityNotFoundException
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.MediaStore
import android.provider.Settings
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.core.graphics.drawable.toBitmap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.text.Collator

/** Yuklu, acilabilir bir uygulama. key = "paket/sinif". */
class AppInfo(val key: String, val label: String, val pkg: String, val cls: String, val icon: ImageBitmap)

object Apps {
    /** Alfabetik sirali tum uygulamalar (kendimiz haric). */
    val all = mutableStateOf<List<AppInfo>>(emptyList())
    private val iconCache = HashMap<String, ImageBitmap>()

    suspend fun load(c: Context) {
        val list = withContext(Dispatchers.IO) {
            val pm = c.packageManager
            val intent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
            val collator = Collator.getInstance(L.locale)
            pm.queryIntentActivities(intent, 0)
                .filter { it.activityInfo.packageName != c.packageName }
                .map { ri ->
                    val pkg = ri.activityInfo.packageName
                    val cls = ri.activityInfo.name
                    val key = "$pkg/$cls"
                    val icon = iconCache.getOrPut(key) {
                        ri.loadIcon(pm).toBitmap(144, 144).asImageBitmap()
                    }
                    AppInfo(key, ri.loadLabel(pm).toString().trim(), pkg, cls, icon)
                }
                .sortedWith { a, b -> collator.compare(a.label, b.label) }
        }
        all.value = list
        if (Prefs.pins == null) Prefs.pins = defaultPins(c, list)
    }

    fun byKey(key: String) = all.value.firstOrNull { it.key == key }

    fun launch(c: Context, a: AppInfo) {
        val i = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
            .setComponent(ComponentName(a.pkg, a.cls))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED)
        try { c.startActivity(i) } catch (_: Exception) { }
    }

    fun appInfo(c: Context, a: AppInfo) = open(c, Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + a.pkg)))
    fun uninstall(c: Context, a: AppInfo) = open(c, Intent(Intent.ACTION_DELETE, Uri.parse("package:" + a.pkg)))

    fun open(c: Context, i: Intent) {
        try { c.startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (_: ActivityNotFoundException) { } catch (_: SecurityException) { }
    }

    fun isPinned(key: String) = Prefs.pins?.contains(key) == true

    fun togglePin(key: String) {
        val p = (Prefs.pins ?: emptyList()).toMutableList()
        if (!p.remove(key)) p.add(key)
        Prefs.pins = p
    }

    fun move(key: String, by: Int) {
        val p = (Prefs.pins ?: return).toMutableList()
        val i = p.indexOf(key)
        val j = (i + by).coerceIn(0, p.size - 1)
        if (i < 0 || i == j) return
        p.removeAt(i); p.add(j, key)
        Prefs.pins = p
    }

    /** Ilk acilis: telefon, mesajlar, tarayici, kamera, ayarlar (bulunabilenler). */
    private fun defaultPins(c: Context, list: List<AppInfo>): List<String> {
        val pm = c.packageManager
        val intents = listOf(
            Intent(Intent.ACTION_DIAL),
            Intent(Intent.ACTION_SENDTO, Uri.parse("smsto:")),
            Intent(Intent.ACTION_VIEW, Uri.parse("https://example.com")),
            Intent(MediaStore.ACTION_IMAGE_CAPTURE),
            Intent(Settings.ACTION_SETTINGS)
        )
        val res = ArrayList<String>()
        for (i in intents) {
            val pkg = try { pm.resolveActivity(i, 0)?.activityInfo?.packageName } catch (_: Exception) { null } ?: continue
            val app = list.firstOrNull { it.pkg == pkg } ?: continue
            if (app.key !in res) res.add(app.key)
        }
        return res
    }
}
