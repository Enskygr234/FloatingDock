package com.winterdeveloper.floatingdock

import android.content.Context
import android.content.SharedPreferences
import androidx.compose.runtime.mutableIntStateOf
import java.util.Locale

/** Kullanici ayarlari (SharedPreferences). Degisince [version] artar, ekranlar yeniden cizilir. */
object Prefs {
    private lateinit var sp: SharedPreferences
    val version = mutableIntStateOf(0)

    fun init(c: Context) {
        if (!::sp.isInitialized) sp = c.applicationContext.getSharedPreferences("floatingdock", Context.MODE_PRIVATE)
    }

    private fun str(k: String, d: String) = sp.getString(k, d) ?: d
    private fun put(k: String, v: String) { sp.edit().putString(k, v).apply(); version.intValue++ }
    private fun putB(k: String, v: Boolean) { sp.edit().putBoolean(k, v).apply(); version.intValue++ }

    /** "system" | "dark" | "light" */
    var theme: String get() = str("theme", "system"); set(v) = put("theme", v)
    /** "frost" (buz mavisi) | "system" (Android'in Material You rengi) */
    var accent: String get() = str("accent", "frost"); set(v) = put("accent", v)
    /** "system" | "tr" | "en" */
    var language: String get() = str("language", "system"); set(v) = put("language", v)
    /** "small" | "medium" | "large" */
    var dockSize: String get() = str("dockSize", "medium"); set(v) = put("dockSize", v)
    var island: Boolean get() = sp.getBoolean("island", true); set(v) = putB("island", v)
    var islandNotifications: Boolean get() = sp.getBoolean("islandNotifs", true); set(v) = putB("islandNotifs", v)

    /** Dock'taki uygulamalar: "paket/sinif" listesi, sirali. null = hic ayarlanmadi (ilk acilis). */
    var pins: List<String>?
        get() = if (sp.contains("pins")) str("pins", "").split('\n').filter { it.isNotBlank() } else null
        set(v) = put("pins", v?.joinToString("\n") ?: "")

    val iconDp: Int get() = when (dockSize) { "small" -> 40; "large" -> 56; else -> 48 }
}

/** Arayuz dili: metinler kodda iki dilde yazilir, L.t("Türkçe", "English"). */
object L {
    val en: Boolean
        get() = when (Prefs.language) {
            "en" -> true
            "tr" -> false
            else -> Locale.getDefault().language != "tr"
        }

    fun t(tr: String, en: String) = if (this.en) en else tr

    val locale: Locale get() = if (en) Locale.US else Locale.forLanguageTag("tr-TR")

    /** Ilk harfi buyuk (Turkcede i -> İ). */
    fun cap(s: String) = if (s.isEmpty()) s else s.substring(0, 1).uppercase(locale) + s.substring(1)
}
