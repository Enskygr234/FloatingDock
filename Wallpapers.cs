using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>
    /// Winter duvar kagitlari: bes hazir tasarim (kodla, ekranin kendi cozunurlugunde cizilir) ve kullanicinin ekledigi gorseller.
    /// Windows masaustu arka planini gercekten degistirir (IDesktopWallpaper, ekran basina), ilk degisiklikten once
    /// eski duvar kagitlarini saklar; "Windows'a geri don" ile hepsi eski hâline gelir.
    /// </summary>
    static class Wallpapers
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatingDock", "Wallpapers");
        public static string CustomDir => Path.Combine(Dir, "custom");
        const string RenderVersion = "v3";

        public static readonly (string id, string name, string desc)[] Builtin =
        {
            ("winter-night", "Winter Night", "Koyu lacivert, ay ışığı ve karlı dağlar"),
            ("arctic-blue", "Arctic Blue", "Buz mavisi kış manzarası ve göl"),
            ("midnight-purple", "Midnight Purple", "Siyah ve mor tonlarda gece, kuzey ışıkları"),
            ("cyber-winter", "Cyber Winter", "Minimal fütüristik şehir ve neon ışıklar"),
            ("pure-minimal", "Pure Minimal", "Sade, premium koyu masaüstü"),
        };

        public static bool IsCustom(string id) => id != null && id.StartsWith("custom:", StringComparison.Ordinal);
        public static string CustomPath(string id) => Path.Combine(CustomDir, id.Substring(7));

        public static string NameOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Windows duvar kâğıdı";
            if (IsCustom(id)) return Path.GetFileNameWithoutExtension(id.Substring(7));
            foreach (var b in Builtin) if (b.id == id) return b.name;
            return id;
        }

        /// <summary>Galeride gosterilecek tum duvar kagitlari: once hazir olanlar, sonra kullanicinin gorselleri.</summary>
        public static List<string> All()
        {
            var list = Builtin.Select(b => b.id).ToList();
            foreach (var f in Settings.Current.WallpaperCustom)
                if (File.Exists(Path.Combine(CustomDir, f))) list.Add("custom:" + f);
            return list;
        }

        // ---------------- kucuk resimler ----------------
        static readonly Dictionary<string, ImageSource> thumbs = new Dictionary<string, ImageSource>();

        /// <summary>Galeri / onizleme icin kucuk resim (onbellekli). Bulunamazsa null.</summary>
        public static ImageSource Thumbnail(string id, int w = 384)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (thumbs.TryGetValue(id, out var t)) return t;
            ImageSource img = null;
            try
            {
                if (IsCustom(id)) img = LoadImage(CustomPath(id), w);
                else if (Builtin.Any(b => b.id == id)) img = Render(id, w, (int)Math.Round(w * 9 / 16.0));
            }
            catch (Exception ex) { Program.Log(ex); }
            if (img != null) thumbs[id] = img;
            return img;
        }

        public static BitmapSource LoadImage(string path, int decodeWidth)
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        // ---------------- Windows ----------------
        public class Monitor
        {
            public string Id;
            public Native.RECT Rect;
            public int Index;
            public int Width => Rect.right - Rect.left;
            public int Height => Rect.bottom - Rect.top;
            public string Label => "Ekran " + (Index + 1) + " · " + Width + "×" + Height + (Rect.left == 0 && Rect.top == 0 ? " (ana ekran)" : "");
        }

        /// <summary>Bagli ekranlar (Windows'un duvar kagidi icin kullandigi monitor yollariyla).</summary>
        public static List<Monitor> Monitors()
        {
            var list = new List<Monitor>();
            try
            {
                var dw = Create();
                uint n = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < n; i++)
                {
                    string id = null;
                    try { id = dw.GetMonitorDevicePathAt(i); } catch { }
                    if (string.IsNullOrEmpty(id)) continue;
                    Native.RECT r;
                    try { r = dw.GetMonitorRECT(id); } catch { continue; } // bagli olmayan monitor
                    if (r.right - r.left <= 0) continue;
                    list.Add(new Monitor { Id = id, Rect = r });
                }
                Marshal.ReleaseComObject(dw);
            }
            catch (Exception ex) { Program.Log(ex); }
            if (list.Count == 0)
                list.Add(new Monitor { Id = "", Rect = new Native.RECT { right = Native.GetSystemMetrics(0), bottom = Native.GetSystemMetrics(1) } });
            list = list.OrderByDescending(m => m.Rect.left == 0 && m.Rect.top == 0).ThenBy(m => m.Rect.left).ThenBy(m => m.Rect.top).ToList();
            for (int i = 0; i < list.Count; i++) list[i].Index = i;
            return list;
        }

        /// <summary>Windows'un o ekranda gosterdigi duvar kagidi dosyasi.</summary>
        public static string WindowsWallpaper(string monitorId)
        {
            try
            {
                var dw = Create();
                var p = dw.GetWallpaper(string.IsNullOrEmpty(monitorId) ? null : monitorId);
                Marshal.ReleaseComObject(dw);
                return p;
            }
            catch { return null; }
        }

        /// <summary>Bir ekranda secili Winter duvar kagidi (ya da null: Windows'un kendisi).</summary>
        public static string Selected(string monitorId) =>
            Settings.Current.WallpaperPerMonitor.TryGetValue(monitorId ?? "", out var v) ? v : null;

        /// <summary>
        /// Duvar kagidini Windows'a uygular. monitorId null: tum ekranlar. Hazir tasarimlar her ekranin kendi cozunurlugunde cizilir.
        /// Hata olursa aciklamasini dondurur.
        /// </summary>
        public static async Task<string> ApplyAsync(string id, string monitorId, string fit)
        {
            var s = Settings.Current;
            var mons = Monitors();
            var targets = monitorId == null ? mons : mons.Where(m => m.Id == monitorId).ToList();
            if (targets.Count == 0) return "Ekran bulunamadı.";
            // dosyalari hazirla (hazir tasarim: ekranin boyutunda PNG)
            var files = new Dictionary<string, string>();
            foreach (var m in targets)
            {
                string f;
                if (IsCustom(id)) f = CustomPath(id);
                else
                {
                    try { f = await EnsureRenderedAsync(id, m.Width, m.Height); }
                    catch (Exception ex) { Program.Log(ex); return "Duvar kâğıdı çizilemedi: " + ex.Message; }
                }
                if (!File.Exists(f)) return "Dosya bulunamadı: " + f;
                files[m.Id] = f;
            }
            BackupOnce(mons);
            int pos = fit == "fit" ? 3 : fit == "center" ? 0 : 4; // DWPOS_FIT / CENTER / FILL
            try
            {
                var dw = Create();
                dw.SetPosition(pos);
                foreach (var kv in files)
                    dw.SetWallpaper(string.IsNullOrEmpty(kv.Key) ? null : kv.Key, kv.Value);
                Marshal.ReleaseComObject(dw);
            }
            catch (Exception ex)
            {
                // eski sistemler: klasik yol (tek duvar kagidi, tum ekranlar)
                Program.Log(ex);
                if (!ClassicSet(files.Values.First(), fit)) return "Windows duvar kâğıdını değiştirmeye izin vermedi.";
            }
            foreach (var m in targets) s.WallpaperPerMonitor[m.Id ?? ""] = id;
            s.WallpaperFit = fit;
            s.Save();
            return null;
        }

        /// <summary>Yalniz konumu (doldur / sigdir / ortala) degistirir.</summary>
        public static void SetFit(string fit)
        {
            var s = Settings.Current;
            s.WallpaperFit = fit;
            s.Save();
            if (s.WallpaperPerMonitor.Count == 0) return; // Winter duvar kagidi uygulanmadiysa Windows'unkine dokunma
            try
            {
                var dw = Create();
                dw.SetPosition(fit == "fit" ? 3 : fit == "center" ? 0 : 4);
                Marshal.ReleaseComObject(dw);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        /// <summary>Ilk Winter duvar kagidindan once Windows'un duvar kagitlarini sakla (bir kez).</summary>
        static void BackupOnce(List<Monitor> mons)
        {
            var s = Settings.Current;
            if (s.WallpaperBackup.Count > 0 || s.WallpaperBackupPosition >= 0) return;
            try
            {
                var dw = Create();
                foreach (var m in mons)
                {
                    string p = null;
                    try { p = dw.GetWallpaper(string.IsNullOrEmpty(m.Id) ? null : m.Id); } catch { }
                    s.WallpaperBackup[m.Id ?? ""] = p ?? "";
                }
                try { s.WallpaperBackupPosition = dw.GetPosition(); } catch { s.WallpaperBackupPosition = 4; }
                Marshal.ReleaseComObject(dw);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                // klasik: kayit defterindeki tek duvar kagidi
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    s.WallpaperBackup[""] = k?.GetValue("WallPaper") as string ?? "";
                    s.WallpaperBackupPosition = 4;
                }
                catch { }
            }
            s.SaveQuiet();
        }

        public static bool HasBackup => Settings.Current.WallpaperBackup.Count > 0;

        /// <summary>Winter'dan once kullanilan Windows duvar kagitlarina don. Basariliysa true.</summary>
        public static bool Restore()
        {
            var s = Settings.Current;
            if (s.WallpaperBackup.Count == 0) { s.WallpaperPerMonitor.Clear(); s.Save(); return true; }
            bool ok = true;
            try
            {
                var dw = Create();
                if (s.WallpaperBackupPosition >= 0) try { dw.SetPosition(s.WallpaperBackupPosition); } catch { }
                var live = Monitors().Select(m => m.Id).ToHashSet();
                foreach (var kv in s.WallpaperBackup)
                {
                    if (kv.Key.Length > 0 && !live.Contains(kv.Key)) continue;
                    try { dw.SetWallpaper(kv.Key.Length == 0 ? null : kv.Key, kv.Value ?? ""); }
                    catch (Exception ex) { Program.Log(ex); ok = false; }
                }
                Marshal.ReleaseComObject(dw);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                ok = s.WallpaperBackup.TryGetValue("", out var p) && ClassicSet(p ?? "", "fill");
            }
            if (ok)
            {
                s.WallpaperBackup.Clear();
                s.WallpaperBackupPosition = -1;
                s.WallpaperPerMonitor.Clear();
                s.Save();
            }
            return ok;
        }

        // ---------------- kullanicinin gorselleri ----------------
        /// <summary>Gorseli Winter klasorune kopyalar ve galeriye ekler; kimligini dondurur.</summary>
        public static string AddCustom(string source)
        {
            Directory.CreateDirectory(CustomDir);
            var ext = Path.GetExtension(source).ToLowerInvariant();
            var stem = new string(Path.GetFileNameWithoutExtension(source).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            if (stem.Length == 0) stem = "gorsel";
            if (stem.Length > 48) stem = stem.Substring(0, 48);
            var name = stem + ext;
            for (int i = 2; File.Exists(Path.Combine(CustomDir, name)); i++) name = stem + " (" + i + ")" + ext;
            File.Copy(source, Path.Combine(CustomDir, name));
            var s = Settings.Current;
            s.WallpaperCustom.Add(name);
            s.Save();
            return "custom:" + name;
        }

        /// <summary>Gorseli galeriden kaldirir. O an masaustunde ise Windows'un kendi kopyasi ekranda kalir.</summary>
        public static void RemoveCustom(string id)
        {
            if (!IsCustom(id)) return;
            var name = id.Substring(7);
            var s = Settings.Current;
            s.WallpaperCustom.Remove(name);
            foreach (var k in s.WallpaperPerMonitor.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) s.WallpaperPerMonitor.Remove(k);
            s.Save();
            thumbs.Remove(id);
            try { File.Delete(Path.Combine(CustomDir, name)); } catch (Exception ex) { Program.Log(ex); }
        }

        // ---------------- cizim ----------------
        static Task<string> EnsureRenderedAsync(string id, int w, int h)
        {
            var file = Path.Combine(Dir, $"{id}_{w}x{h}_{RenderVersion}.png");
            if (File.Exists(file)) return Task.FromResult(file);
            var tcs = new TaskCompletionSource<string>();
            // buyuk ciz: kendi STA is parcaciginda (arayuz donmasin)
            var t = new Thread(() =>
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    var bmp = Render(id, w, h);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    var tmp = file + ".tmp";
                    using (var fs = File.Create(tmp)) enc.Save(fs);
                    File.Move(tmp, file, true);
                    tcs.TrySetResult(file);
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return tcs.Task;
        }

        /// <summary>Hazir duvar kagidini verilen boyutta cizer.</summary>
        public static BitmapSource Render(string id, int w, int h)
        {
            var root = new ContainerVisual();
            var rnd = new Random(StableSeed(id));
            switch (id)
            {
                case "arctic-blue": ArcticBlue(root, rnd, w, h); break;
                case "midnight-purple": MidnightPurple(root, rnd, w, h); break;
                case "cyber-winter": CyberWinter(root, rnd, w, h); break;
                case "pure-minimal": PureMinimal(root, rnd, w, h); break;
                default: WinterNight(root, rnd, w, h); break;
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            rtb.Freeze();
            return rtb;
        }

        static int StableSeed(string s) { int h = 17; foreach (var c in s) h = h * 31 + c; return h & 0x7fffffff; }

        static DrawingContext Layer(ContainerVisual root, out DrawingVisual v, double blur = 0, double opacity = 1)
        {
            v = new DrawingVisual();
            if (blur > 0) v.Effect = new BlurEffect { Radius = blur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
            v.Opacity = opacity;
            root.Children.Add(v);
            return v.RenderOpen();
        }

        static Color C(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        static LinearGradientBrush Vertical(double y0, double y1, params (double at, Color c)[] stops)
        {
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, y0), EndPoint = new Point(0, y1) };
            foreach (var (at, c) in stops) b.GradientStops.Add(new GradientStop(c, at));
            b.Freeze();
            return b;
        }

        static RadialGradientBrush Glow(Color c, byte alpha)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha / 3), c.R, c.G, c.B), 0.45));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }

        static void Stars(DrawingContext dc, Random r, double w, double maxY, int count, double scale, Color c)
        {
            for (int i = 0; i < count; i++)
            {
                double x = r.NextDouble() * w, y = Math.Pow(r.NextDouble(), 1.4) * maxY;
                double rad = (0.5 + Math.Pow(r.NextDouble(), 4) * 1.6) * scale;
                byte a = (byte)(60 + r.Next(195) * (1 - y / maxY * 0.6));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)), null, new Point(x, y), rad, rad);
            }
        }

        /// <summary>Daglar: orta nokta kaydirmasiyla tek bir sirt cizgisi; alttan kapanan sekil.</summary>
        static Geometry Ridge(Random r, double w, double baseY, double topY, double rough, double bottom, int detail = 8)
        {
            int n = (1 << detail) + 1;
            var ys = new double[n];
            double range = baseY - topY;
            ys[0] = baseY - r.NextDouble() * range * 0.55;
            ys[n - 1] = baseY - r.NextDouble() * range * 0.55;
            double disp = range * 0.9;
            for (int step = n - 1; step > 1; step /= 2)
            {
                for (int i = step / 2; i < n - 1; i += step)
                    ys[i] = (ys[i - step / 2] + ys[i + step / 2]) / 2 + (r.NextDouble() * 2 - 1) * disp * 0.5;
                disp *= rough;
            }
            // en yuksek tepe topY'ye, en alcak nokta tabana yakin gelsin (sekil korunur, duz "masa" tepeler olmaz)
            double mn = ys.Min(), mx = ys.Max(), span = Math.Max(1e-6, mx - mn), low = baseY - range * 0.12;
            for (int i = 0; i < n; i++) ys[i] = topY + (ys[i] - mn) / span * (low - topY);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, bottom), true, true);
                for (int i = 0; i < n; i++) ctx.LineTo(new Point(w * i / (n - 1), ys[i]), true, false);
                ctx.LineTo(new Point(w, bottom), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Karli dag: tepeler kar renginde, asagisi kaya.</summary>
        static Brush Snowy(double topY, double baseY, Color snow, Color rock, double snowLine = 0.3) =>
            Vertical(topY, baseY, (0, snow), (snowLine, snow), (snowLine + 0.22, rock), (1, rock));

        static void Pines(DrawingContext dc, Random r, double x0, double x1, double baseY, double minH, double maxH, Brush fill, double density)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                for (double x = x0; x < x1; x += (0.35 + r.NextDouble() * 0.7) * minH * density)
                {
                    double hgt = minH + r.NextDouble() * (maxH - minH), wid = hgt * (0.28 + r.NextDouble() * 0.08);
                    double y = baseY + r.NextDouble() * minH * 0.3;
                    for (int k = 0; k < 3; k++) // uc kat
                    {
                        double ty = y - hgt + k * hgt * 0.22, by = y - hgt * (0.48 - k * 0.2), hw = wid * (0.45 + k * 0.3) / 2;
                        ctx.BeginFigure(new Point(x, ty), true, true);
                        ctx.LineTo(new Point(x + hw, by), true, false);
                        ctx.LineTo(new Point(x - hw, by), true, false);
                    }
                    ctx.BeginFigure(new Point(x - wid * 0.06, y), true, true);
                    ctx.LineTo(new Point(x + wid * 0.06, y), true, false);
                    ctx.LineTo(new Point(x + wid * 0.06, y - hgt * 0.1), true, false);
                    ctx.LineTo(new Point(x - wid * 0.06, y - hgt * 0.1), true, false);
                }
            }
            g.FillRule = FillRule.Nonzero; // ust uste binen katlar bosluk birakmasin
            g.Freeze();
            dc.DrawGeometry(fill, null, g);
        }

        /// <summary>Harf araligi acik metin (logo yazisi), ortalanmis.</summary>
        static void Tracked(DrawingContext dc, string text, double size, FontWeight weight, Brush fill, double cx, double y, double tracking)
        {
            var tf = new Typeface(Theme.Ui, FontStyles.Normal, weight, FontStretches.Normal);
            var parts = text.Select(ch => new FormattedText(ch.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, fill, 1.0)).ToList();
            double total = parts.Sum(p => p.WidthIncludingTrailingWhitespace) + tracking * size * (parts.Count - 1);
            double x = cx - total / 2;
            foreach (var p in parts) { dc.DrawText(p, new Point(x, y)); x += p.WidthIncludingTrailingWhitespace + tracking * size; }
        }

        static void Logo(DrawingContext dc, double cx, double cy, double size, Brush fill)
        {
            dc.PushTransform(new TranslateTransform(cx - size / 2, cy - size / 2));
            dc.PushTransform(new ScaleTransform(size / 24, size / 24));
            dc.DrawGeometry(fill, null, WinterLogo.Geometry);
            dc.Pop(); dc.Pop();
        }

        /// <summary>Gol: daglarin aynadaki yansimasi + su tonu.</summary>
        static void Reflect(DrawingContext dc, double w, double h, double lakeY, IEnumerable<(Geometry g, Brush b)> shapes, double opacity, Brush water)
        {
            dc.DrawRectangle(water, null, new Rect(0, lakeY, w, h - lakeY));
            dc.PushClip(new RectangleGeometry(new Rect(0, lakeY, w, h - lakeY)));
            dc.PushOpacity(opacity);
            dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 2 * lakeY));
            foreach (var (g, b) in shapes) dc.DrawGeometry(b, null, g);
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        // ---- 1. Winter Night ----
        static void WinterNight(ContainerVisual root, Random r, double w, double h)
        {
            double u = h / 1080.0, lake = h * 0.765;
            var dc = Layer(root, out _);
            dc.DrawRectangle(Vertical(0, lake, (0, C(0x03050E)), (0.42, C(0x0A1231)), (0.78, C(0x1B2C63)), (1, C(0x2D4C96))), null, new Rect(0, 0, w, lake));
            Stars(dc, r, w, h * 0.62, (int)(w * h / 7000), u, C(0xDDE7FF));
            dc.DrawEllipse(Glow(C(0x3D6BFF), 120), null, new Point(w * 0.5, h * 0.36), h * 0.42, h * 0.42);
            dc.Close();

            // isik halkasi: bulanik parilti + ince keskin cizgi
            var ringC = new Point(w * 0.5, h * 0.37); double rr = h * 0.255;
            var ring = Vertical(ringC.Y - rr, ringC.Y + rr, (0, C(0xBFD9FF)), (0.45, C(0x4C86FF)), (1, C(0x4C86FF, 0)));
            var gl = Layer(root, out _, 26 * u);
            gl.DrawEllipse(null, new Pen(ring, 14 * u), ringC, rr, rr);
            gl.Close();
            var rl = Layer(root, out _);
            rl.DrawEllipse(null, new Pen(ring, 2.6 * u), ringC, rr, rr);
            rl.Close();

            // daglar ve gol
            var back = Ridge(r, w, lake, h * 0.5, 0.6, lake + 2);
            var backB = Snowy(h * 0.5, lake, C(0xDCE6FF), C(0x18244F), 0.3);
            var front = Ridge(r, w, lake + h * 0.01, h * 0.62, 0.58, lake + 2);
            var frontB = Snowy(h * 0.62, lake, C(0x8B9CCB), C(0x0B1333), 0.1);
            var m = Layer(root, out _);
            m.DrawGeometry(backB, null, back);
            m.DrawRectangle(Vertical(h * 0.62, lake, (0, C(0x8FA6E6, 0)), (0.7, C(0x8FA6E6, 50)), (1, C(0x8FA6E6, 0))), null, new Rect(0, h * 0.62, w, lake - h * 0.62));
            m.DrawGeometry(frontB, null, front);
            Pines(m, r, -10, w * 0.36, lake + 2 * u, 30 * u, 92 * u, Brushes.Black, 1.0);
            Pines(m, r, w * 0.64, w + 10, lake + 2 * u, 30 * u, 100 * u, new SolidColorBrush(C(0x04060F)), 1.0);
            Reflect(m, w, h, lake, new[] { (back, (Brush)backB), (front, frontB) }, 0.3,
                Vertical(lake, h, (0, C(0x1C2E66)), (1, C(0x03060F))));
            // ay isigi suda
            m.DrawRectangle(new SolidColorBrush(C(0xD6E3FF, 70)), null, new Rect(0, lake, w, 1.2 * u));
            m.Close();
            var streak = Layer(root, out _, 10 * u, 0.8);
            streak.DrawRectangle(Vertical(lake, h, (0, C(0xBFD9FF, 150)), (1, C(0xBFD9FF, 0))), null, new Rect(w * 0.5 - 16 * u, lake, 32 * u, (h - lake) * 0.9));
            streak.Close();
            m = Layer(root, out _);
            // logo ve yazi
            var logoB = Vertical(h * 0.27, h * 0.37, (0, C(0x9CCBFF)), (1, C(0x2F6BFF)));
            Logo(m, w * 0.5, h * 0.315, h * 0.1, logoB);
            Tracked(m, "WINTER DEVELOPER", h * 0.034, FontWeights.Bold, new SolidColorBrush(C(0xA9CBFF)), w * 0.5, h * 0.38, 0.3);
            Tracked(m, "Dream · Code · Create", h * 0.021, FontWeights.SemiBold, new SolidColorBrush(C(0x7EA2E0)), w * 0.5, h * 0.43, 0.18);
            m.Close();
        }

        // ---- 2. Arctic Blue ----
        static void ArcticBlue(ContainerVisual root, Random r, double w, double h)
        {
            double u = h / 1080.0, lake = h * 0.7;
            var dc = Layer(root, out _);
            dc.DrawRectangle(Vertical(0, lake, (0, C(0x5E9ED3)), (0.55, C(0xA9D5F0)), (1, C(0xE9F6FC))), null, new Rect(0, 0, w, lake));
            dc.DrawEllipse(Glow(C(0xFFFFFF), 230), null, new Point(w * 0.74, h * 0.2), h * 0.26, h * 0.26);
            dc.DrawEllipse(new SolidColorBrush(C(0xFFFFFF, 240)), null, new Point(w * 0.74, h * 0.2), h * 0.045, h * 0.045);
            dc.Close();
            var cl = Layer(root, out _, 30 * u, 0.85);
            for (int i = 0; i < 9; i++)
            {
                double cx = r.NextDouble() * w, cy = h * (0.08 + r.NextDouble() * 0.28), cw = w * (0.08 + r.NextDouble() * 0.14);
                cl.DrawEllipse(new SolidColorBrush(C(0xFFFFFF, 200)), null, new Point(cx, cy), cw, cw * 0.18);
            }
            cl.Close();
            var back = Ridge(r, w, lake, h * 0.34, 0.6, lake + 2);
            var backB = Snowy(h * 0.34, lake, C(0xFFFFFF), C(0x6F95C0), 0.36);
            var front = Ridge(r, w, lake + h * 0.01, h * 0.5, 0.56, lake + 2);
            var frontB = Snowy(h * 0.5, lake, C(0xF4F9FF), C(0x41699A), 0.3);
            var m = Layer(root, out _);
            m.DrawGeometry(backB, null, back);
            m.DrawRectangle(Vertical(h * 0.5, lake, (0, C(0xFFFFFF, 0)), (1, C(0xFFFFFF, 120))), null, new Rect(0, h * 0.5, w, lake - h * 0.5));
            m.DrawGeometry(frontB, null, front);
            var pine = new SolidColorBrush(C(0x24485F));
            Pines(m, r, -10, w * 0.3, lake + 2 * u, 26 * u, 80 * u, pine, 1.1);
            Pines(m, r, w * 0.75, w + 10, lake + 2 * u, 26 * u, 84 * u, pine, 1.1);
            Reflect(m, w, h, lake, new[] { (back, (Brush)backB), (front, frontB) }, 0.32,
                Vertical(lake, h, (0, C(0xBFE3F4)), (1, C(0x6FA8D2))));
            // buz parcalari
            for (int i = 0; i < 12; i++)
            {
                double fx = r.NextDouble() * w, fy = lake + (h - lake) * (0.2 + r.NextDouble() * 0.8), fw = (30 + r.NextDouble() * 110) * u * (0.5 + (fy - lake) / (h - lake));
                m.DrawEllipse(new SolidColorBrush(C(0xFFFFFF, (byte)(150 + r.Next(90)))), null, new Point(fx, fy), fw, fw * 0.12);
            }
            m.DrawRectangle(new SolidColorBrush(C(0xFFFFFF, 160)), null, new Rect(0, lake, w, 2 * u));
            m.Close();
        }

        // ---- 3. Midnight Purple ----
        static void MidnightPurple(ContainerVisual root, Random r, double w, double h)
        {
            double u = h / 1080.0, ground = h * 0.78;
            var dc = Layer(root, out _);
            dc.DrawRectangle(Vertical(0, ground, (0, C(0x000000)), (0.45, C(0x0D0517)), (0.82, C(0x2A0E4C)), (1, C(0x4A1A7A))), null, new Rect(0, 0, w, ground));
            Stars(dc, r, w, h * 0.6, (int)(w * h / 9000), u, C(0xEBDDFF));
            dc.DrawEllipse(Glow(C(0xF2E6FF), 90), null, new Point(w * 0.8, h * 0.16), h * 0.08, h * 0.08);
            var moon = Geometry.Combine(new EllipseGeometry(new Point(w * 0.8, h * 0.16), h * 0.03, h * 0.03),
                new EllipseGeometry(new Point(w * 0.8 + h * 0.014, h * 0.15), h * 0.027, h * 0.027), GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(new SolidColorBrush(C(0xF5EDFF)), null, moon);
            dc.Close();
            // kuzey isiklari: bulanik dalgali seritler
            Geometry Wave(double y, double amp, double phase)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(-w * 0.1, y), false, false);
                    for (int i = 0; i <= 6; i++)
                    {
                        double x0 = -w * 0.1 + w * 1.2 * i / 6, x1 = -w * 0.1 + w * 1.2 * (i + 0.5) / 6, x2 = -w * 0.1 + w * 1.2 * (i + 1) / 6;
                        c.QuadraticBezierTo(new Point(x1, y + amp * Math.Sin(phase + i * 1.3)), new Point(x2, y + amp * 0.3 * Math.Cos(phase + i)), true, true);
                    }
                }
                g.Freeze();
                return g;
            }
            var hor = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            hor.GradientStops.Add(new GradientStop(C(0x6A2CFF, 0), 0));
            hor.GradientStops.Add(new GradientStop(C(0x8B4DFF, 230), 0.3));
            hor.GradientStops.Add(new GradientStop(C(0xE45CFF, 220), 0.6));
            hor.GradientStops.Add(new GradientStop(C(0x5FD1FF, 160), 0.85));
            hor.GradientStops.Add(new GradientStop(C(0x5FD1FF, 0), 1));
            hor.Freeze();
            var a1 = Layer(root, out _, 55 * u, 0.9);
            a1.DrawGeometry(null, new Pen(hor, 120 * u), Wave(h * 0.3, h * 0.08, 0.4));
            a1.DrawGeometry(null, new Pen(hor, 70 * u), Wave(h * 0.42, h * 0.06, 2.1));
            a1.Close();
            var a2 = Layer(root, out _, 8 * u, 0.55);
            a2.DrawGeometry(null, new Pen(hor, 6 * u), Wave(h * 0.3, h * 0.08, 0.4));
            a2.DrawGeometry(null, new Pen(hor, 4 * u), Wave(h * 0.42, h * 0.06, 2.1));
            a2.Close();
            var gl = Layer(root, out _, 30 * u);
            gl.DrawRectangle(new SolidColorBrush(C(0x9B5CFF, 200)), null, new Rect(0, ground - 6 * u, w, 12 * u));
            gl.Close();
            var m = Layer(root, out _);
            m.DrawGeometry(new SolidColorBrush(C(0x170A2B)), null, Ridge(r, w, ground, h * 0.6, 0.5, h + 2));
            m.DrawGeometry(new SolidColorBrush(C(0x0A0414)), null, Ridge(r, w, ground + h * 0.03, h * 0.68, 0.48, h + 2));
            Pines(m, r, -10, w + 10, ground + h * 0.06, 22 * u, 70 * u, new SolidColorBrush(C(0x030106)), 1.6);
            m.DrawRectangle(Vertical(ground + h * 0.05, h, (0, C(0x030106)), (1, C(0x000000))), null, new Rect(0, ground + h * 0.05, w, h - ground));
            m.Close();
        }

        // ---- 4. Cyber Winter ----
        static void CyberWinter(ContainerVisual root, Random r, double w, double h)
        {
            double u = h / 1080.0, horizon = h * 0.7;
            var dc = Layer(root, out _);
            dc.DrawRectangle(Vertical(0, horizon, (0, C(0x02030A)), (0.55, C(0x0A1033)), (1, C(0x2A0F4A))), null, new Rect(0, 0, w, horizon));
            Stars(dc, r, w, h * 0.45, (int)(w * h / 16000), u, C(0xBFEFFF));
            // neon gunes (yatay seritli)
            var sunC = new Point(w * 0.5, horizon - h * 0.02); double sr = h * 0.19;
            dc.DrawEllipse(Glow(C(0xFF3FD0), 120), null, sunC, sr * 2.1, sr * 2.1);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, horizon)));
            var sunG = new EllipseGeometry(sunC, sr, sr);
            var cut = new GeometryGroup();
            for (int i = 0; i < 7; i++) { double y = sunC.Y - sr * 0.05 + i * sr * 0.16; cut.Children.Add(new RectangleGeometry(new Rect(0, y, w, sr * 0.03 + i * sr * 0.012))); }
            dc.DrawGeometry(Vertical(sunC.Y - sr, sunC.Y + sr * 0.3, (0, C(0xFFD36B)), (0.55, C(0xFF5FB0)), (1, C(0xB43CFF))), null,
                Geometry.Combine(sunG, cut, GeometryCombineMode.Exclude, null));
            dc.Pop();
            dc.Close();
            // sehir
            var city = Layer(root, out _);
            var body = new SolidColorBrush(C(0x060918));
            var edge = new Pen(new SolidColorBrush(C(0x1C2560)), 1.2 * u);
            var cyan = C(0x3FF3FF); var pink = C(0xFF4FD8);
            for (int layer = 0; layer < 2; layer++)
            {
                double x = -20 * u;
                while (x < w)
                {
                    double bw = w * (0.018 + r.NextDouble() * (layer == 0 ? 0.03 : 0.045));
                    double bh = h * (layer == 0 ? 0.12 + r.NextDouble() * 0.3 : 0.05 + r.NextDouble() * 0.18);
                    if (Math.Abs(x + bw / 2 - w * 0.5) < w * 0.12 && layer == 0) bh *= 0.5; // gunes gorunsun
                    var rect = new Rect(x, horizon - bh, bw, bh + 1);
                    city.DrawRectangle(layer == 0 ? new SolidColorBrush(C(0x0B1130)) : body, edge, rect);
                    // pencereler
                    for (double wy = rect.Top + 8 * u; wy < horizon - 6 * u; wy += 9 * u)
                        for (double wx = rect.Left + 4 * u; wx < rect.Right - 5 * u; wx += 7 * u)
                            if (r.NextDouble() < 0.16)
                            {
                                var wc = r.NextDouble() < 0.65 ? cyan : pink;
                                city.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(90 + r.Next(150)), wc.R, wc.G, wc.B)), null, new Rect(wx, wy, 2.6 * u, 3.4 * u));
                            }
                    if (r.NextDouble() < 0.25) // cati neonu
                        city.DrawRectangle(new SolidColorBrush(r.NextDouble() < 0.5 ? cyan : pink), null, new Rect(rect.Left, rect.Top, bw, 2 * u));
                    if (r.NextDouble() < 0.15) city.DrawRectangle(body, null, new Rect(rect.Left + bw * 0.45, rect.Top - h * 0.04, 2 * u, h * 0.04));
                    x += bw + (layer == 0 ? 6 : 2) * u;
                }
            }
            city.Close();
            var glow = Layer(root, out _, 24 * u);
            glow.DrawRectangle(new SolidColorBrush(C(0x21D4FD, 220)), null, new Rect(0, horizon - 4 * u, w, 8 * u));
            glow.Close();
            // zemin: perspektif izgara
            var g = Layer(root, out _);
            g.DrawRectangle(Vertical(horizon, h, (0, C(0x0A0F2A)), (1, C(0x020309))), null, new Rect(0, horizon, w, h - horizon));
            var gp = new Pen(new SolidColorBrush(C(0x21D4FD, 120)), 1.4 * u);
            for (int i = 1; i < 16; i++) { double t = Math.Pow(i / 16.0, 2.1); double y = horizon + (h - horizon) * t; g.DrawLine(gp, new Point(0, y), new Point(w, y)); }
            var vp = new Point(w * 0.5, horizon);
            for (int i = -24; i <= 24; i++) g.DrawLine(gp, vp, new Point(w * 0.5 + i * w * 0.09, h));
            g.DrawRectangle(new SolidColorBrush(C(0xBFF6FF)), null, new Rect(0, horizon - 1 * u, w, 2 * u));
            // kar
            for (int i = 0; i < (int)(w * h / 5200); i++)
            {
                double x = r.NextDouble() * w, y = r.NextDouble() * h, rad = (0.6 + r.NextDouble() * 1.8) * u;
                g.DrawEllipse(new SolidColorBrush(C(0xFFFFFF, (byte)(70 + r.Next(160)))), null, new Point(x, y), rad, rad);
            }
            g.Close();
            var big = Layer(root, out _, 3 * u, 0.7);
            for (int i = 0; i < 40; i++) { double rad = (3 + r.NextDouble() * 4) * u; big.DrawEllipse(new SolidColorBrush(C(0xFFFFFF, 180)), null, new Point(r.NextDouble() * w, r.NextDouble() * h), rad, rad); }
            big.Close();
        }

        // ---- 5. Pure Minimal ----
        static void PureMinimal(ContainerVisual root, Random r, double w, double h)
        {
            double u = h / 1080.0;
            var dc = Layer(root, out _);
            dc.DrawRectangle(new SolidColorBrush(C(0x0C0F14)), null, new Rect(0, 0, w, h));
            dc.DrawEllipse(Glow(C(0x2A3A55), 200), null, new Point(w * 0.5, -h * 0.15), w * 0.75, h * 0.9);
            dc.DrawEllipse(Glow(C(0x1B2A44), 140), null, new Point(w * 0.92, h * 1.05), w * 0.4, h * 0.5);
            dc.DrawRectangle(new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(C(0xFFFFFF, 0), 0), new GradientStop(C(0xFFFFFF, 34), 0.5), new GradientStop(C(0xFFFFFF, 0), 1)
            }, 0), null, new Rect(w * 0.2, h * 0.64, w * 0.6, Math.Max(1, 1 * u)));
            Logo(dc, w * 0.5, h * 0.5, h * 0.075, new SolidColorBrush(C(0xFFFFFF, 30)));
            Tracked(dc, "WINTER", h * 0.016, FontWeights.Bold, new SolidColorBrush(C(0xFFFFFF, 46)), w * 0.5, h * 0.565, 0.6);
            // ince tane (premium his)
            for (int i = 0; i < (int)(w * h / 2500); i++)
                dc.DrawRectangle(new SolidColorBrush(C(0xFFFFFF, (byte)(3 + r.Next(6)))), null, new Rect(r.NextDouble() * w, r.NextDouble() * h, 1, 1));
            dc.Close();
        }

        // ---------------- Windows API ----------------
        static IDesktopWallpaper Create() => (IDesktopWallpaper)new DesktopWallpaperClass();

        static bool ClassicSet(string path, string fit)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                {
                    k.SetValue("WallpaperStyle", fit == "fit" ? "6" : fit == "center" ? "0" : "10");
                    k.SetValue("TileWallpaper", "0");
                }
                return SystemParametersInfo(0x0014, 0, path, 0x01 | 0x02);
            }
            catch (Exception ex) { Program.Log(ex); return false; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, string pv, uint winIni);

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        class DesktopWallpaperClass { }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            Native.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(int position);
            int GetPosition();
            void SetSlideshow(IntPtr items);
            IntPtr GetSlideshow();
            void SetSlideshowOptions(int options, uint slideshowTick);
            void GetSlideshowOptions(out int options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
            int GetStatus();
            void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }
    }
}
