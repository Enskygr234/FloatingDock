using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FloatingDock
{
    /// <summary>Kabuk ikonlarini arka planda (STA is parcaciginda) yukler; arayuz donmaz.</summary>
    static class ShellIcons
    {
        static readonly ConcurrentDictionary<string, ImageSource> cache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly BlockingCollection<(string path, int size, TaskCompletionSource<ImageSource> tcs)> queue =
            new BlockingCollection<(string, int, TaskCompletionSource<ImageSource>)>();
        static Thread worker;

        public static ImageSource Cached(string path) => cache.TryGetValue(path, out var i) ? i : null;

        public static Task<ImageSource> GetAsync(string path, int size = 48)
        {
            if (cache.TryGetValue(path, out var c)) return Task.FromResult(c);
            var tcs = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            EnsureWorker();
            queue.Add((path, size, tcs));
            return tcs.Task;
        }

        static void EnsureWorker()
        {
            if (worker != null) return;
            lock (queue)
            {
                if (worker != null) return;
                worker = new Thread(() =>
                {
                    foreach (var (path, size, tcs) in queue.GetConsumingEnumerable())
                    {
                        if (!cache.TryGetValue(path, out var img)) { img = Load(path, size); cache[path] = img; }
                        tcs.TrySetResult(img);
                    }
                }) { IsBackground = true, Name = "ShellIcons" };
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
        }

        static ImageSource Load(string path, int size)
        {
            try
            {
                Native.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(Native.IShellItemImageFactory).GUID, out var f);
                if (f == null) return null;
                try
                {
                    if (f.GetImage(new Native.SIZE { cx = size, cy = size }, 0x4 | 0x1, out IntPtr hbm) == 0 && hbm != IntPtr.Zero)
                    {
                        var bs = FromHBitmap(hbm);
                        Native.DeleteObject(hbm);
                        return bs;
                    }
                }
                finally { Marshal.ReleaseComObject(f); }
            }
            catch { }
            return null;
        }

        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            Native.GetObject(hbm, Marshal.SizeOf<Native.BITMAP>(), out var bm);
            int w = bm.bmWidth, h = bm.bmHeight;
            var bi = new Native.BITMAPINFOHEADER { biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var bits = new byte[w * h * 4];
            var dc = Native.GetDC(IntPtr.Zero);
            Native.GetDIBits(dc, hbm, 0, (uint)h, bits, ref bi, 0);
            Native.ReleaseDC(IntPtr.Zero, dc);
            bool anyAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
            var bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
            bs.Freeze();
            return bs;
        }
    }
}
