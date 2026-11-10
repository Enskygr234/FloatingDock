package com.winterdeveloper.floatingdock

import android.app.PendingIntent
import android.media.session.MediaController
import android.os.Handler
import android.os.Looper
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

class MediaNow(val title: String, val artist: String, val art: ImageBitmap?, val playing: Boolean, val app: String)
class Note(val app: String, val title: String, val text: String, val icon: ImageBitmap?, val intent: PendingIntent?)

/** Ada icin paylasilan durum (ana is parcaciginda degisir). */
object IslandState {
    val media = mutableStateOf<MediaNow?>(null)
    /** muzik duraklatildiktan sonra 8 sn daha gorunsun */
    val showPaused = mutableStateOf(false)
    val note = mutableStateOf<Note?>(null)
    val expanded = mutableStateOf(false)
    var controller: MediaController? = null

    private val handler = Handler(Looper.getMainLooper())
    private val clearNote = Runnable { note.value = null }
    private val hidePaused = Runnable { showPaused.value = false }
    private val collapse = Runnable { expanded.value = false }

    fun setMedia(m: MediaNow?) {
        val was = media.value?.playing == true
        media.value = m
        if (was && m != null && !m.playing) {
            showPaused.value = true
            handler.removeCallbacks(hidePaused)
            handler.postDelayed(hidePaused, 8000)
        }
    }

    fun show(n: Note) {
        note.value = n
        handler.removeCallbacks(clearNote)
        handler.postDelayed(clearNote, 6000)
    }

    fun dismissNote() { handler.removeCallbacks(clearNote); note.value = null }

    fun expand() {
        expanded.value = true
        handler.removeCallbacks(collapse)
        handler.postDelayed(collapse, 6000)
    }

    fun toggleExpand() = if (expanded.value) { handler.removeCallbacks(collapse); expanded.value = false } else expand()
}

private val IslandBlack = Color(0xF5060609)

/** Ekranin ustunde: calan muzik ve gelen bildirimler (Dynamic Island tarzi). Her zaman koyu. */
@Composable
fun Island() {
    val media by IslandState.media
    val showPaused by IslandState.showPaused
    val note by IslandState.note
    val expanded by IslandState.expanded
    val m = media
    val state = when {
        note != null -> "notif"
        m != null && (m.playing || showPaused || expanded) -> if (expanded) "expanded" else "compact"
        else -> "none"
    }
    AnimatedContent(targetState = state, transitionSpec = { fadeIn(tween(220)) togetherWith fadeOut(tween(160)) }, label = "island") { s ->
        when (s) {
            "notif" -> note?.let { NotifView(it) }
            "compact" -> m?.let { Compact(it) }
            "expanded" -> m?.let { Expanded(it) }
            else -> Box(Modifier.size(0.dp))
        }
    }
}

@Composable
private fun Pill(width: Int, height: Int, radius: Int, onClick: () -> Unit, content: @Composable () -> Unit) {
    val shape = RoundedCornerShape(radius.dp)
    Box(
        Modifier.padding(4.dp).width(width.dp).height(height.dp).clip(shape).background(IslandBlack, shape)
            .border(1.dp, Color.White.copy(alpha = 0.12f), shape).clickable(onClick = onClick),
        contentAlignment = Alignment.CenterStart
    ) { content() }
}

@Composable
private fun Art(m: MediaNow, size: Int, radius: Int) {
    val shape = RoundedCornerShape(radius.dp)
    if (m.art != null) Image(m.art, null, Modifier.size(size.dp).clip(shape), contentScale = ContentScale.Crop)
    else Box(Modifier.size(size.dp).clip(shape).background(Color.White.copy(alpha = 0.1f)))
}

@Composable
private fun Compact(m: MediaNow) {
    Pill(300, 40, 20, onClick = { IslandState.expand() }) {
        Row(Modifier.padding(horizontal = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Art(m, 28, 14)
            Spacer(Modifier.width(10.dp))
            Txt(if (m.artist.isBlank()) m.title else m.title + "  ·  " + m.artist, 13, Color.White, modifier = Modifier.weight(1f))
            Spacer(Modifier.width(8.dp))
            Equalizer(m.playing)
            Spacer(Modifier.width(8.dp))
        }
    }
}

@Composable
private fun Equalizer(playing: Boolean) {
    val inf = rememberInfiniteTransition(label = "eq")
    val durations = intArrayOf(420, 580, 360, 500)
    Row(Modifier.height(16.dp), horizontalArrangement = Arrangement.spacedBy(2.dp), verticalAlignment = Alignment.Bottom) {
        for (d in durations) {
            val h by inf.animateFloat(3f, 15f, infiniteRepeatable(tween(d), RepeatMode.Reverse), label = "bar")
            Box(Modifier.width(3.dp).height((if (playing) h else 3f).dp).clip(RoundedCornerShape(2.dp)).background(Frost))
        }
    }
}

@Composable
private fun Expanded(m: MediaNow) {
    Pill(340, 132, 30, onClick = { IslandState.toggleExpand() }) {
        Column(Modifier.padding(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Art(m, 56, 14)
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Txt(m.app, 11, Color.White.copy(alpha = 0.6f))
                    Txt(m.title, 15, Color.White, FontWeight.SemiBold)
                    Txt(m.artist, 13, Color.White.copy(alpha = 0.75f))
                }
            }
            Row(Modifier.padding(top = 8.dp).fillMaxHeight(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.weight(1f))
                Ctrl("prev") { IslandState.controller?.transportControls?.skipToPrevious(); IslandState.expand() }
                Spacer(Modifier.width(18.dp))
                Ctrl(if (m.playing) "pause" else "play") {
                    val tc = IslandState.controller?.transportControls
                    if (m.playing) tc?.pause() else tc?.play()
                    IslandState.expand()
                }
                Spacer(Modifier.width(18.dp))
                Ctrl("next") { IslandState.controller?.transportControls?.skipToNext(); IslandState.expand() }
                Spacer(Modifier.weight(1f))
            }
        }
    }
}

@Composable
private fun Ctrl(kind: String, onClick: () -> Unit) {
    Box(Modifier.size(40.dp).clip(RoundedCornerShape(10.dp)).clickable(onClick = onClick), contentAlignment = Alignment.Center) {
        MediaGlyph(kind, Color.White, 22.dp)
    }
}

@Composable
private fun NotifView(n: Note) {
    Pill(340, 76, 28, onClick = {
        try { n.intent?.send() } catch (_: Exception) { }
        IslandState.dismissNote()
    }) {
        Row(Modifier.padding(horizontal = 14.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(40.dp).clip(RoundedCornerShape(12.dp)).background(Color.White.copy(alpha = 0.08f)), contentAlignment = Alignment.Center) {
                if (n.icon != null) Image(n.icon, null, Modifier.size(28.dp))
            }
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Txt(n.app, 11, Color.White.copy(alpha = 0.6f))
                Txt(n.title, 14, Color.White, FontWeight.SemiBold)
                if (n.text.isNotBlank()) Txt(n.text, 12, Color.White.copy(alpha = 0.75f))
            }
        }
    }
}
