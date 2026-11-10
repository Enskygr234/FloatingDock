package com.winterdeveloper.floatingdock

import android.os.Build
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** Masaustu surumuyle ayni renk sistemi: tek vurgu rengi + "murekkep" (beyaz ya da siyah) saydamliklari. */
class Palette(
    val dark: Boolean,
    val accent: Color,
    val onAccent: Color,
    /** yazi ve yuzeylerin ana rengi: koyu temada beyaz, acikta siyah */
    val ink: Color,
    /** panel zemini (cam) */
    val panel: Color,
    /** dock dolgusu */
    val dock: Color,
) {
    fun ink(a: Float) = ink.copy(alpha = a)
    val text get() = ink(if (dark) 0.96f else 0.90f)
    val text2 get() = ink(if (dark) 0.75f else 0.65f)
    val text3 get() = ink(if (dark) 0.55f else 0.49f)
    val stroke get() = ink(if (dark) 0.12f else 0.15f)
    val hover get() = ink(if (dark) 0.10f else 0.07f)
}

val LocalPalette = staticCompositionLocalOf { palette(true, Color(0xFF8FC4FF)) }
val Frost = Color(0xFF8FC4FF)

fun palette(dark: Boolean, accent: Color) = Palette(
    dark = dark,
    accent = accent,
    onAccent = if (dark) Color(0xFF0B1A2B) else Color.White,
    ink = if (dark) Color.White else Color(0xFF101418),
    panel = if (dark) Color(0xE6161A22) else Color(0xEBF6F7FA),
    dock = if (dark) Color(0xC7161A22) else Color(0xD9F6F7FA),
)

@Composable
fun FdTheme(content: @Composable () -> Unit) {
    Prefs.version.intValue // ayar degisince yeniden ciz
    val dark = when (Prefs.theme) { "dark" -> true; "light" -> false; else -> isSystemInDarkTheme() }
    val ctx = LocalContext.current
    val accent = if (Prefs.accent == "system" && Build.VERSION.SDK_INT >= 31)
        Color(ctx.getColor(if (dark) android.R.color.system_accent1_200 else android.R.color.system_accent1_600))
    else if (dark) Frost else Color(0xFF1F6FC5)
    val p = palette(dark, accent)
    val scheme = if (dark) darkColorScheme(primary = accent, onPrimary = p.onAccent, surface = Color(0xFF161A22))
                 else lightColorScheme(primary = accent, onPrimary = p.onAccent)
    MaterialTheme(colorScheme = scheme) {
        CompositionLocalProvider(LocalPalette provides p, content = content)
    }
}

/** Cam panel: yari saydam zemin + ince kenar. */
fun Modifier.glass(p: Palette, shape: Shape, fill: Color = p.panel) =
    this.clip(shape).background(fill, shape).border(1.dp, p.stroke, shape)

/** Basinca hafifce kuculen tiklanabilir alan. */
@Composable
fun Modifier.pressable(onClick: () -> Unit): Modifier {
    val src = remember { MutableInteractionSource() }
    val pressed by src.collectIsPressedAsState()
    val s by animateFloatAsState(if (pressed) 0.92f else 1f, label = "press")
    return this.scale(s).clickable(interactionSource = src, indication = null, onClick = onClick)
}

@Composable
fun Txt(text: String, size: Int = 14, color: Color = LocalPalette.current.text, weight: FontWeight = FontWeight.Normal, modifier: Modifier = Modifier, maxLines: Int = 1) =
    Text(text, modifier = modifier, color = color, fontSize = size.sp, fontWeight = weight, maxLines = maxLines, softWrap = maxLines > 1,
        overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)

/** Baslat simgesi: vurgu renginde yuvarlak kare, icinde dort kucuk kare. */
@Composable
fun StartGlyph(size: Dp) {
    val p = LocalPalette.current
    Box(Modifier.size(size).clip(RoundedCornerShape(size * 0.3f)).background(p.accent), contentAlignment = Alignment.Center) {
        Canvas(Modifier.size(size * 0.48f)) {
            val c = this.size.width
            val s = c * 0.42f
            val gap = c - 2 * s
            val alphas = floatArrayOf(1f, 0.75f, 0.75f, 1f)
            for (i in 0 until 4) {
                val x = (i % 2) * (s + gap); val y = (i / 2) * (s + gap)
                drawRoundRect(p.onAccent.copy(alpha = alphas[i]), Offset(x, y), androidx.compose.ui.geometry.Size(s, s),
                    androidx.compose.ui.geometry.CornerRadius(s * 0.25f))
            }
        }
    }
}

/** Basit cizilmis medya simgeleri (ek ikon kutuphanesi gerekmesin). */
@Composable
fun MediaGlyph(kind: String, color: Color, size: Dp = 22.dp) {
    Canvas(Modifier.size(size)) {
        val w = this.size.width; val h = this.size.height
        fun tri(x0: Float, x1: Float) = Path().apply { moveTo(x0, h * 0.18f); lineTo(x1, h * 0.5f); lineTo(x0, h * 0.82f); close() }
        when (kind) {
            "play" -> drawPath(tri(w * 0.25f, w * 0.85f), color)
            "pause" -> {
                drawRoundRect(color, Offset(w * 0.24f, h * 0.18f), androidx.compose.ui.geometry.Size(w * 0.17f, h * 0.64f), androidx.compose.ui.geometry.CornerRadius(w * 0.04f))
                drawRoundRect(color, Offset(w * 0.59f, h * 0.18f), androidx.compose.ui.geometry.Size(w * 0.17f, h * 0.64f), androidx.compose.ui.geometry.CornerRadius(w * 0.04f))
            }
            "next" -> {
                drawPath(tri(w * 0.18f, w * 0.68f), color)
                drawLine(color, Offset(w * 0.76f, h * 0.2f), Offset(w * 0.76f, h * 0.8f), strokeWidth = w * 0.1f, cap = StrokeCap.Round)
            }
            "prev" -> {
                drawPath(Path().apply { moveTo(w * 0.82f, h * 0.18f); lineTo(w * 0.32f, h * 0.5f); lineTo(w * 0.82f, h * 0.82f); close() }, color)
                drawLine(color, Offset(w * 0.24f, h * 0.2f), Offset(w * 0.24f, h * 0.8f), strokeWidth = w * 0.1f, cap = StrokeCap.Round)
            }
            "search" -> {
                drawCircle(color, radius = w * 0.28f, center = Offset(w * 0.43f, h * 0.43f), style = Stroke(w * 0.1f))
                drawLine(color, Offset(w * 0.64f, h * 0.64f), Offset(w * 0.86f, h * 0.86f), strokeWidth = w * 0.1f, cap = StrokeCap.Round)
            }
        }
    }
}

/** Bolumlu secici (Sistem | Koyu | Acik). */
@Composable
fun Segmented(labels: List<String>, selected: Int, onSelect: (Int) -> Unit) {
    val p = LocalPalette.current
    Row(Modifier.clip(RoundedCornerShape(10.dp)).background(p.ink(0.06f)).border(1.dp, p.stroke, RoundedCornerShape(10.dp)).padding(2.dp)) {
        labels.forEachIndexed { i, l ->
            val on = i == selected
            Box(
                Modifier.clip(RoundedCornerShape(8.dp)).background(if (on) p.accent else Color.Transparent)
                    .clickable { if (!on) onSelect(i) }.padding(horizontal = 12.dp, vertical = 7.dp)
            ) {
                Txt(l, 13, if (on) p.onAccent else p.text2, if (on) FontWeight.SemiBold else FontWeight.Normal)
            }
        }
    }
}
