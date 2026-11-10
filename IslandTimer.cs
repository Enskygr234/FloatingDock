using System;

namespace FloatingDock
{
    /// <summary>Adada gosterilen zamanlayici / kronometre. Takvim panelinden baslatilir; durum yalniz bellekte.</summary>
    static class IslandTimer
    {
        public enum Kind { None, Countdown, Stopwatch }

        public static Kind Mode { get; private set; } = Kind.None;
        public static bool Paused { get; private set; }
        /// <summary>Geri sayimin toplam suresi (etiket icin: "5 dk zamanlayıcı").</summary>
        public static TimeSpan Total { get; private set; }

        static DateTime startedAt;     // son "devam" ani
        static TimeSpan banked;        // duraklatmadan onceki birikmis sure

        public static event Action Changed;
        /// <summary>Geri sayim bitti (UI is parcaciginda, Tick'ten cagrilir).</summary>
        public static event Action Finished;

        public static bool Active => Mode != Kind.None;

        /// <summary>Kronometrede gecen, geri sayimda kalan sure.</summary>
        public static TimeSpan Value
        {
            get
            {
                var elapsed = banked + (Paused || Mode == Kind.None ? TimeSpan.Zero : DateTime.Now - startedAt);
                if (Mode == Kind.Countdown) { var left = Total - elapsed; return left < TimeSpan.Zero ? TimeSpan.Zero : left; }
                return elapsed;
            }
        }

        /// <summary>Geri sayimda ilerleme 0..1 (dolan kisim).</summary>
        public static double Progress => Mode == Kind.Countdown && Total.TotalSeconds > 0 ? 1 - Value.TotalSeconds / Total.TotalSeconds : 0;

        public static void StartCountdown(TimeSpan t)
        {
            Mode = Kind.Countdown; Total = t; banked = TimeSpan.Zero; startedAt = DateTime.Now; Paused = false;
            Changed?.Invoke();
        }

        public static void StartStopwatch()
        {
            Mode = Kind.Stopwatch; Total = TimeSpan.Zero; banked = TimeSpan.Zero; startedAt = DateTime.Now; Paused = false;
            Changed?.Invoke();
        }

        public static void TogglePause()
        {
            if (!Active) return;
            if (Paused) { startedAt = DateTime.Now; Paused = false; }
            else { banked += DateTime.Now - startedAt; Paused = true; }
            Changed?.Invoke();
        }

        public static void AddMinute()
        {
            if (Mode != Kind.Countdown) return;
            Total += TimeSpan.FromMinutes(1);
            Changed?.Invoke();
        }

        public static void Stop()
        {
            if (!Active) return;
            Mode = Kind.None; Paused = false; banked = TimeSpan.Zero;
            Changed?.Invoke();
        }

        /// <summary>Ada her tikte cagirir: geri sayim bittiyse kapatip haber verir.</summary>
        public static void Tick()
        {
            if (Mode == Kind.Countdown && !Paused && Value <= TimeSpan.Zero)
            {
                Stop();
                Finished?.Invoke();
            }
        }

        public static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

        public static string Label => Mode == Kind.Stopwatch ? "Kronometre"
            : Total.TotalMinutes >= 1 && Total.Seconds == 0 ? (int)Total.TotalMinutes + " dk zamanlayıcı" : "Zamanlayıcı";
    }
}
