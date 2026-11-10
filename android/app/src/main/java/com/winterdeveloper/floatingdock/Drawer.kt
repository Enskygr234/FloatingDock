package com.winterdeveloper.floatingdock

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectVerticalDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.Icon
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Uygulama menusu: selamlama, arama, harf gruplu tum uygulamalar (masaustundeki Baslat menusunun karsiligi). */
@Composable
fun Drawer(onClose: () -> Unit) {
    val p = LocalPalette.current
    val ctx = LocalContext.current
    val apps by Apps.all
    var q by remember { mutableStateOf("") }
    val query = q.trim()
    val results = remember(query, apps) { if (query.isEmpty()) emptyList() else search(apps, query) }

    fun web() = Apps.open(ctx, Intent(Intent.ACTION_VIEW, Uri.parse("https://www.google.com/search?q=" + Uri.encode(query))))
    fun launchApp(a: AppInfo) { Apps.launch(ctx, a); onClose() }

    Box(
        Modifier.fillMaxSize().background(p.panel)
            .pointerInput(Unit) { detectTapGestures { } } // dokunuslar arkadaki ana ekrana gecmesin
    ) {
        Column(Modifier.fillMaxSize().statusBarsPadding().navigationBarsPadding().imePadding().padding(horizontal = 16.dp)) {
            // ust: selamlama + ayarlar + kapat. Asagi kaydirinca kapanir.
            Row(
                Modifier.fillMaxWidth().padding(top = 18.dp, bottom = 14.dp).pointerInput(Unit) {
                    var total = 0f
                    detectVerticalDragGestures(onDragStart = { total = 0f }, onDragEnd = { if (total > 80) onClose() }) { _, dy -> total += dy }
                },
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column(Modifier.weight(1f)) {
                    Txt(greeting(), 24, p.text, FontWeight.SemiBold)
                    Txt(if (apps.isEmpty()) "" else apps.size.toString() + L.t(" uygulama yüklü", " apps installed"), 13, p.text3)
                }
                RoundButton({ Icon(Icons.Filled.Settings, L.t("Ayarlar", "Settings"), tint = p.text2) }) {
                    ctx.startActivity(Intent(ctx, SettingsActivity::class.java))
                }
                Spacer(Modifier.width(6.dp))
                RoundButton({ Icon(Icons.Filled.Close, L.t("Kapat", "Close"), tint = p.text2) }, onClose)
            }

            // arama
            val shape = RoundedCornerShape(22.dp)
            Row(
                Modifier.fillMaxWidth().height(46.dp).glass(p, shape, p.ink(0.06f)).padding(horizontal = 14.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                MediaGlyph("search", p.text2, 20.dp)
                Spacer(Modifier.width(10.dp))
                Box(Modifier.weight(1f), contentAlignment = Alignment.CenterStart) {
                    if (q.isEmpty()) Txt(L.t("Uygulama ara", "Search apps"), 15, p.text3)
                    BasicTextField(
                        value = q, onValueChange = { q = it }, singleLine = true,
                        textStyle = TextStyle(color = p.text, fontSize = 15.sp),
                        cursorBrush = SolidColor(p.accent),
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                        keyboardActions = KeyboardActions(onSearch = { results.firstOrNull()?.let { launchApp(it) } ?: web() }),
                        modifier = Modifier.fillMaxWidth()
                    )
                }
            }
            Spacer(Modifier.height(14.dp))

            if (query.isEmpty()) AllApps(apps) { launchApp(it) }
            else LazyColumn(Modifier.fillMaxSize()) {
                items(results, key = { it.key }) { a -> ResultRow(a) { launchApp(a) } }
                item {
                    Row(
                        Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).pressable { web(); onClose() }.padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Box(Modifier.size(40.dp), contentAlignment = Alignment.Center) { MediaGlyph("search", p.accent, 22.dp) }
                        Spacer(Modifier.width(14.dp))
                        Column {
                            Txt(if (L.en) "Search the web for \"$query\"" else "\"$query\" için web'de ara", 15, p.text)
                            Txt(L.t("Tarayıcıda açılır", "Opens in your browser"), 12, p.text3)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun RoundButton(icon: @Composable () -> Unit, onClick: () -> Unit) {
    val p = LocalPalette.current
    Box(Modifier.size(40.dp).clip(RoundedCornerShape(20.dp)).background(p.ink(0.06f)).pressable(onClick), contentAlignment = Alignment.Center) { icon() }
}

/** Harf basliklariyla 4 sutunlu izgara. */
@Composable
private fun AllApps(apps: List<AppInfo>, run: (AppInfo) -> Unit) {
    val p = LocalPalette.current
    val groups = remember(apps) {
        apps.groupBy { a -> a.label.firstOrNull()?.let { if (it.isLetter()) it.uppercase(L.locale) else "#" } ?: "#" }
    }
    LazyVerticalGrid(GridCells.Fixed(4), Modifier.fillMaxSize()) {
        for ((letter, list) in groups) {
            item(span = { GridItemSpan(maxLineSpan) }, key = "h_$letter") {
                Row(Modifier.padding(start = 6.dp, top = 14.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    Txt(letter, 15, p.accent, FontWeight.SemiBold)
                    Spacer(Modifier.width(10.dp))
                    Box(Modifier.weight(1f).height(1.dp).background(p.stroke))
                }
            }
            items(list, key = { it.key }) { a -> GridApp(a) { run(a) } }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun GridApp(a: AppInfo, onClick: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    val p = LocalPalette.current
    Box {
        Column(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp))
                .combinedClickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onLongClick = { menu = true }, onClick = onClick)
                .padding(vertical = 10.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Image(a.icon, null, Modifier.size(50.dp).clip(RoundedCornerShape(14.dp)))
            Spacer(Modifier.height(6.dp))
            androidx.compose.material3.Text(a.label, color = p.text, fontSize = 12.sp, maxLines = 1, textAlign = TextAlign.Center,
                overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis, modifier = Modifier.padding(horizontal = 4.dp))
        }
        AppMenu(a, menu, inDock = false) { menu = false }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ResultRow(a: AppInfo, onClick: () -> Unit) {
    var menu by remember { mutableStateOf(false) }
    val p = LocalPalette.current
    Box {
        Row(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp))
                .combinedClickable(interactionSource = remember { MutableInteractionSource() }, indication = null, onLongClick = { menu = true }, onClick = onClick)
                .padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Image(a.icon, null, Modifier.size(40.dp).clip(RoundedCornerShape(11.dp)))
            Spacer(Modifier.width(14.dp))
            Column {
                Txt(a.label, 15, p.text)
                Txt(L.t("Uygulama", "App"), 12, p.text3)
            }
        }
        AppMenu(a, menu, inDock = false) { menu = false }
    }
}

/** Masaustundeki gibi puanlama: basinda > kelime basinda > icinde > harfler sirayla ("wa" -> WhatsApp). */
private fun search(apps: List<AppInfo>, q: String): List<AppInfo> {
    val k = q.lowercase(L.locale)
    fun score(name: String): Int {
        val n = name.lowercase(L.locale)
        if (n.startsWith(k)) return 0
        if (n.split(' ', '-', '.').any { it.startsWith(k) }) return 1
        if (n.contains(k)) return 2
        var i = 0
        for (ch in n) if (i < k.length && ch == k[i]) i++
        return if (i == k.length && k.length >= 2) 3 else 99
    }
    return apps.map { it to score(it.label) }.filter { it.second < 99 }.sortedWith(compareBy({ it.second }, { it.first.label.length })).map { it.first }.take(12)
}
