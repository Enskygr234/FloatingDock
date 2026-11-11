using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FloatingDock
{
    class WeatherDay { public DateTime Date, Sunrise, Sunset; public int Code, RainChance; public double Max, Min, UvMax; }
    class WeatherHour { public DateTime Time; public int Code, RainChance; public double Temp; public bool IsDay; }

    class WeatherData
    {
        public double Temp, Feels, Wind, WindDir, Humidity, Pressure, Uv;
        public int Code;
        public bool IsDay = true;
        public string Place;
        public List<WeatherDay> Days = new List<WeatherDay>();
        public List<WeatherHour> Hours = new List<WeatherHour>();
        /// <summary>Onumuzdeki 2 saatin 15 dakikalik yagis miktarlari (mm), ilki MinutelyStart.</summary>
        public double[] Minutely = Array.Empty<double>();
        public DateTime MinutelyStart;
        /// <summary>Avrupa hava kalitesi indeksi; bilinmiyorsa -1.</summary>
        public int Aqi = -1;
        public double Pm25 = -1;
        public DateTime Fetched;
        /// <summary>Yerin UTC farki: API saatleri o yerin yerel saatidir.</summary>
        public TimeSpan UtcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        /// <summary>O yerdeki su anki saat (sehir baska saat diliminde olabilir).</summary>
        public DateTime Now => DateTime.UtcNow + UtcOffset;
    }

    /// <summary>Kayitli bir yer (sehir).</summary>
    class WeatherPlace
    {
        public string Name; public double Lat, Lon;
        public string Serialize() => Name + "|" + Lat.ToString(CultureInfo.InvariantCulture) + "|" + Lon.ToString(CultureInfo.InvariantCulture);
        public static WeatherPlace Parse(string s)
        {
            var p = (s ?? "").Split('|');
            if (p.Length != 3 || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var la)
                || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)) return null;
            return new WeatherPlace { Name = p[0], Lat = la, Lon = lo };
        }
    }

    /// <summary>
    /// Hava durumu (open-meteo.com, anahtarsiz). Yalniz ayarlarda sehir girildiyse ya da otomatik konum aciksa calisir:
    /// sehir adi bir kez koordinata cevrilir, sonra her 15 dakikada bir tahmin alinir. Otomatik konumda koordinat
    /// Windows konum servisinden gelir; yer adi icin koordinat api.bigdatacloud.net'e sorulur.
    /// </summary>
    static class Weather
    {
        static readonly HttpClient http = CreateClient();
        static System.Threading.Timer timer;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly System.Threading.SemaphoreSlim busy = new System.Threading.SemaphoreSlim(1, 1);
        static DateTime located = DateTime.MinValue;

        public static WeatherData Current { get; private set; }
        public static string LastError { get; private set; }
        /// <summary>Arka plan is parcacigindan gelebilir; dinleyen Dispatcher'a gecmeli.</summary>
        public static event Action Changed;
        /// <summary>Yagis yaklasiyor: (baslik, metin). Arka plan is parcacigindan gelir.</summary>
        public static event Action<string, string> RainAlert;
        public static bool Enabled => Settings.Current.WeatherAuto || !string.IsNullOrWhiteSpace(Settings.Current.WeatherCity);

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FloatingDock/1.4");
            return c;
        }

        public static void Start()
        {
            timer ??= new System.Threading.Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(15));
        }

        // Save -> Changed olayi arayuze dokunur: UI is parcaciginda kaydet
        static void SaveOnUi(Settings s) => Application.Current?.Dispatcher.BeginInvoke(new Action(s.Save));

        public static async Task<bool> RefreshAsync()
        {
            var s = Settings.Current;
            if (!Enabled)
            {
                if (Current != null) { Current = null; Changed?.Invoke(); }
                return false;
            }
            await busy.WaitAsync();
            try
            {
                if (s.WeatherAuto)
                {
                    // konumu saatte bir yenile (dizustu bilgisayar yer degistirebilir)
                    if ((s.WeatherLat == 0 && s.WeatherLon == 0) || (DateTime.Now - located).TotalMinutes > 60)
                    {
                        var p = await LocateAsync();
                        if (p == null) { Changed?.Invoke(); return false; }
                        located = DateTime.Now;
                        bool moved = Math.Abs(p.Value.lat - s.WeatherLat) > 0.02 || Math.Abs(p.Value.lon - s.WeatherLon) > 0.02;
                        if (moved || string.IsNullOrEmpty(s.WeatherPlace))
                        {
                            s.WeatherLat = Math.Round(p.Value.lat, 3); s.WeatherLon = Math.Round(p.Value.lon, 3);
                            s.WeatherPlace = await ReverseAsync(s.WeatherLat, s.WeatherLon) ?? "Konumum";
                            SaveOnUi(s);
                        }
                    }
                }
                else if (s.WeatherLat == 0 && s.WeatherLon == 0)
                {
                    var g = await GeocodeAsync(s.WeatherCity);
                    if (g == null) { LastError = "Şehir bulunamadı"; Changed?.Invoke(); return false; }
                    s.WeatherLat = g.Value.lat; s.WeatherLon = g.Value.lon; s.WeatherPlace = g.Value.name;
                    SaveOnUi(s);
                }
                double lat = s.WeatherLat, lon = s.WeatherLon;
                var url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(Inv) + "&longitude=" + lon.ToString(Inv) +
                          "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,wind_direction_10m,relative_humidity_2m,pressure_msl,uv_index" +
                          "&hourly=temperature_2m,weather_code,precipitation_probability,is_day&forecast_hours=25" +
                          "&minutely_15=precipitation&forecast_minutely_15=8" +
                          "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max,precipitation_probability_max&timezone=auto&forecast_days=7";
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                var root = doc.RootElement;
                var cur = root.GetProperty("current");
                var d = new WeatherData
                {
                    Temp = Num(cur, "temperature_2m"),
                    Feels = Num(cur, "apparent_temperature"),
                    Wind = Num(cur, "wind_speed_10m"),
                    WindDir = Num(cur, "wind_direction_10m"),
                    Humidity = Num(cur, "relative_humidity_2m"),
                    Pressure = Num(cur, "pressure_msl"),
                    Uv = Num(cur, "uv_index"),
                    Code = (int)Num(cur, "weather_code"),
                    IsDay = Num(cur, "is_day") == 1,
                    Place = string.IsNullOrEmpty(s.WeatherPlace) ? s.WeatherCity : s.WeatherPlace,
                    Fetched = DateTime.Now
                };
                if (root.TryGetProperty("utc_offset_seconds", out var off) && off.ValueKind == JsonValueKind.Number) d.UtcOffset = TimeSpan.FromSeconds(off.GetDouble());

                var daily = root.GetProperty("daily");
                var dt = daily.GetProperty("time");
                for (int i = 0; i < dt.GetArrayLength(); i++)
                {
                    if (!IsNum(daily, "weather_code", i) || !IsNum(daily, "temperature_2m_max", i)) continue;
                    d.Days.Add(new WeatherDay
                    {
                        Date = DateTime.ParseExact(dt[i].GetString(), "yyyy-MM-dd", Inv),
                        Code = (int)At(daily, "weather_code", i), Max = At(daily, "temperature_2m_max", i), Min = At(daily, "temperature_2m_min", i),
                        UvMax = At(daily, "uv_index_max", i), RainChance = (int)At(daily, "precipitation_probability_max", i),
                        Sunrise = Time(daily, "sunrise", i), Sunset = Time(daily, "sunset", i)
                    });
                }

                var hourly = root.GetProperty("hourly");
                var ht = hourly.GetProperty("time");
                for (int i = 0; i < ht.GetArrayLength(); i++)
                {
                    if (!IsNum(hourly, "temperature_2m", i)) continue;
                    d.Hours.Add(new WeatherHour
                    {
                        Time = Time(hourly, "time", i), Temp = At(hourly, "temperature_2m", i), Code = (int)At(hourly, "weather_code", i),
                        RainChance = (int)At(hourly, "precipitation_probability", i), IsDay = At(hourly, "is_day", i) == 1
                    });
                }

                if (root.TryGetProperty("minutely_15", out var mq) && mq.TryGetProperty("time", out var mt) && mt.GetArrayLength() > 0)
                {
                    d.MinutelyStart = Time(mq, "time", 0);
                    d.Minutely = Enumerable.Range(0, mt.GetArrayLength()).Select(i => At(mq, "precipitation", i)).ToArray();
                }

                // hava kalitesi ayri bir servis: alinamazsa panelde gosterilmez
                try
                {
                    var aq = "https://air-quality-api.open-meteo.com/v1/air-quality?latitude=" + lat.ToString(Inv) + "&longitude=" + lon.ToString(Inv) + "&current=european_aqi,pm2_5";
                    using var adoc = JsonDocument.Parse(await http.GetStringAsync(aq));
                    var ac = adoc.RootElement.GetProperty("current");
                    if (ac.TryGetProperty("european_aqi", out var a) && a.ValueKind == JsonValueKind.Number) d.Aqi = (int)Math.Round(a.GetDouble());
                    if (ac.TryGetProperty("pm2_5", out var pm) && pm.ValueKind == JsonValueKind.Number) d.Pm25 = pm.GetDouble();
                }
                catch { }

                Current = d;
                LastError = null;
                Changed?.Invoke();
                CheckRain(d);
                return true;
            }
            catch (Exception ex)
            {
                // internet yoksa son bilinen veriyi tut; hatayi yalniz bir kez gunluge yaz
                if (LastError != ex.Message) Program.Log(new Exception("Hava durumu: " + ex.Message));
                LastError = ex.Message;
                Changed?.Invoke();
                return false;
            }
            finally { busy.Release(); }
        }

        // ---------------- JSON yardimcilari ----------------
        static double Num(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        static bool IsNum(JsonElement o, string name, int i) => o.TryGetProperty(name, out var a) && i < a.GetArrayLength() && a[i].ValueKind == JsonValueKind.Number;
        static double At(JsonElement o, string name, int i) => IsNum(o, name, i) ? o.GetProperty(name)[i].GetDouble() : 0;
        static DateTime Time(JsonElement o, string name, int i)
        {
            if (!o.TryGetProperty(name, out var a) || i >= a.GetArrayLength() || a[i].ValueKind != JsonValueKind.String) return DateTime.MinValue;
            return DateTime.TryParseExact(a[i].GetString(), "yyyy-MM-dd'T'HH:mm", Inv, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;
        }

        // ---------------- yagmur uyarisi ----------------
        static DateTime rainAlertQuietUntil = DateTime.MinValue;

        /// <summary>Su an kuru ve onumuzdeki bir saat icinde yagis basliyorsa bir kez uyar (ayni yagis icin 3 saat susar).</summary>
        static void CheckRain(WeatherData d)
        {
            if (!Settings.Current.WeatherRainAlert) return;
            if (RainStart(d, out int minutes, out bool snow) && minutes <= 60 && DateTime.Now >= rainAlertQuietUntil)
            {
                rainAlertQuietUntil = DateTime.Now.AddHours(3);
                var what = snow ? "Kar" : "Yağmur";
                var body = minutes <= 5 ? d.Place + " · birkaç dakika içinde başlıyor"
                                        : d.Place + " · yaklaşık " + (Math.Max(5, Math.Round(minutes / 5.0) * 5)).ToString("0", Inv) + " dk içinde başlıyor";
                RainAlert?.Invoke(what + " yaklaşıyor", body);
            }
        }

        /// <summary>Su an yagis yoksa ve onumuzdeki 2 saatte basliyorsa kac dakika sonra. Su an yagiyorsa false.</summary>
        public static bool RainStart(WeatherData d, out int minutes, out bool snow)
        {
            minutes = 0; snow = false;
            if (d == null || d.Minutely.Length == 0) return false;
            var now = d.Now;
            bool first = true;
            for (int i = 0; i < d.Minutely.Length; i++)
            {
                var start = d.MinutelyStart.AddMinutes(15 * i);
                if (start.AddMinutes(15) <= now) continue;   // gecmis dilim
                bool wet = d.Minutely[i] >= 0.2;
                if (first) { first = false; if (wet || IsWetCode(d.Code)) return false; continue; } // su an yagiyor: uyari yok
                if (wet)
                {
                    minutes = Math.Max(1, (int)Math.Round((start - now).TotalMinutes));
                    var hour = d.Hours.FirstOrDefault(h => h.Time.Date == start.Date && h.Time.Hour == start.Hour);
                    snow = hour != null && Classify(hour.Code) == Sky.Snow;
                    return true;
                }
            }
            return false;
        }

        static bool IsWetCode(int code) => Classify(code) is Sky.Rain or Sky.Snow or Sky.Storm;

        /// <summary>Onumuzdeki 2 saatin ozeti: "2 saat yağış beklenmiyor", "≈35 dk sonra yağmur", "≈20 dk sonra diniyor".</summary>
        public static string NowcastText(WeatherData d)
        {
            if (d == null || d.Minutely.Length == 0) return null;
            if (RainStart(d, out int m, out bool snow)) return "≈" + m + " dk sonra " + (snow ? "kar" : "yağmur");
            var now = d.Now;
            int cur = -1;
            for (int i = 0; i < d.Minutely.Length; i++) if (d.MinutelyStart.AddMinutes(15 * (i + 1)) > now) { cur = i; break; }
            bool wetNow = IsWetCode(d.Code) || (cur >= 0 && d.Minutely[cur] >= 0.2);
            if (!wetNow) return "2 saat yağış beklenmiyor";
            for (int i = Math.Max(cur, 0) + 1; i < d.Minutely.Length; i++)
                if (d.Minutely[i] < 0.1) return "≈" + Math.Max(1, (int)(d.MinutelyStart.AddMinutes(15 * i) - now).TotalMinutes) + " dk sonra diniyor";
            return "Yağış sürüyor";
        }

        // ---------------- yerler ----------------
        public static List<WeatherPlace> Saved => Settings.Current.WeatherSaved.Select(WeatherPlace.Parse).Where(p => p != null).ToList();

        /// <summary>Ayarlardan yeni sehir: bulunursa kaydeder (kayitli yerlere de ekler) ve hemen tahmini alir. Bulunamazsa null, bos ise kapatir.</summary>
        public static async Task<string> SetCityAsync(string city)
        {
            var s = Settings.Current;
            city = (city ?? "").Trim();
            if (city.Length == 0)
            {
                s.WeatherCity = ""; s.WeatherPlace = ""; s.WeatherLat = 0; s.WeatherLon = 0; s.WeatherAuto = false; s.Save();
                Current = null; LastError = null; Changed?.Invoke();
                return "";
            }
            var g = await GeocodeAsync(city);
            if (g == null) return null;
            Use(new WeatherPlace { Name = g.Value.name, Lat = g.Value.lat, Lon = g.Value.lon }, city);
            await RefreshAsync();
            return g.Value.name;
        }

        /// <summary>Kayitli bir yere gec (UI is parcacigindan).</summary>
        public static void UsePlace(WeatherPlace p)
        {
            Use(p, p.Name);
            _ = RefreshAsync();
        }

        static void Use(WeatherPlace p, string city)
        {
            var s = Settings.Current;
            s.WeatherAuto = false;
            s.WeatherCity = city; s.WeatherLat = p.Lat; s.WeatherLon = p.Lon; s.WeatherPlace = p.Name;
            var list = Saved;
            if (!list.Any(x => Same(x, p))) { list.Add(p); if (list.Count > 6) list.RemoveAt(0); }
            s.WeatherSaved = list.Select(x => x.Serialize()).ToList();
            s.Save();
            Start();
        }

        public static void RemovePlace(WeatherPlace p)
        {
            var s = Settings.Current;
            s.WeatherSaved = Saved.Where(x => !Same(x, p)).Select(x => x.Serialize()).ToList();
            s.Save();
            Changed?.Invoke();
        }

        public static bool IsActive(WeatherPlace p) => !Settings.Current.WeatherAuto && Same(p, new WeatherPlace { Lat = Settings.Current.WeatherLat, Lon = Settings.Current.WeatherLon });
        static bool Same(WeatherPlace a, WeatherPlace b) => Math.Abs(a.Lat - b.Lat) < 0.01 && Math.Abs(a.Lon - b.Lon) < 0.01;

        /// <summary>Otomatik konumu ac. Basarida null, olmazsa kullaniciya gosterilecek hata metni.</summary>
        public static async Task<string> UseAutoAsync()
        {
            var p = await LocateAsync();
            if (p == null) return LastError ?? "Konum alınamadı";
            var s = Settings.Current;
            s.WeatherAuto = true;
            s.WeatherLat = Math.Round(p.Value.lat, 3); s.WeatherLon = Math.Round(p.Value.lon, 3);
            s.WeatherPlace = await ReverseAsync(s.WeatherLat, s.WeatherLon) ?? "Konumum";
            located = DateTime.Now;
            s.Save();
            Start();
            await RefreshAsync();
            return null;
        }

        /// <summary>Windows konum servisi. Kapaliysa (Ayarlar > Gizlilik > Konum) null.</summary>
        static async Task<(double lat, double lon)?> LocateAsync()
        {
            try
            {
                var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
                if (access != Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
                {
                    LastError = "Konum izni kapalı (Ayarlar › Gizlilik › Konum)";
                    return null;
                }
                var g = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default };
                var pos = await g.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15));
                var c = pos.Coordinate.Point.Position;
                return (c.Latitude, c.Longitude);
            }
            catch (Exception ex) { LastError = "Konum alınamadı: " + ex.Message; return null; }
        }

        /// <summary>Koordinattan yer adi (ilce / sehir).</summary>
        static async Task<string> ReverseAsync(double lat, double lon)
        {
            try
            {
                var url = "https://api.bigdatacloud.net/data/reverse-geocode-client?latitude=" + lat.ToString(Inv) + "&longitude=" + lon.ToString(Inv) + "&localityLanguage=tr";
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                var r = doc.RootElement;
                foreach (var k in new[] { "city", "locality", "principalSubdivision" })
                    if (r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())) return v.GetString();
            }
            catch { }
            return null;
        }

        static async Task<(double lat, double lon, string name)?> GeocodeAsync(string city)
        {
            try
            {
                var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=tr&format=json&name=" + Uri.EscapeDataString(city);
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return null;
                var r = res[0];
                var name = r.GetProperty("name").GetString();
                return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name);
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }

        // ---------------- metinler ----------------
        public static string UvText(double uv) => uv < 3 ? "Düşük" : uv < 6 ? "Orta" : uv < 8 ? "Yüksek" : uv < 11 ? "Çok yüksek" : "Aşırı";
        public static string AqiText(int aqi) => aqi < 0 ? "" : aqi <= 20 ? "İyi" : aqi <= 40 ? "Makul" : aqi <= 60 ? "Orta" : aqi <= 80 ? "Kötü" : aqi <= 100 ? "Çok kötü" : "Son derece kötü";
        /// <summary>Ruzgarin geldigi yon (8 yon): K, KD, D, GD, G, GB, B, KB.</summary>
        public static string Compass(double deg) => new[] { "K", "KD", "D", "GD", "G", "GB", "B", "KB" }[(int)Math.Round((((deg % 360) + 360) % 360) / 45) % 8];

        // ---------------- WMO hava kodlari ----------------
        public static string Describe(int code) => code switch
        {
            0 => "Açık",
            1 => "Çoğunlukla açık",
            2 => "Parçalı bulutlu",
            3 => "Kapalı",
            45 or 48 => "Sisli",
            51 or 53 or 55 => "Çisenti",
            56 or 57 => "Dondurucu çisenti",
            61 => "Hafif yağmur",
            63 => "Yağmurlu",
            65 => "Şiddetli yağmur",
            66 or 67 => "Dondurucu yağmur",
            71 => "Hafif kar",
            73 => "Kar yağışlı",
            75 => "Yoğun kar",
            77 => "Kar taneleri",
            80 => "Hafif sağanak",
            81 => "Sağanak",
            82 => "Şiddetli sağanak",
            85 or 86 => "Kar sağanağı",
            95 => "Gök gürültülü fırtına",
            96 or 99 => "Dolu ve fırtına",
            _ => ""
        };

        enum Sky { Clear, Partly, Cloudy, Fog, Rain, Snow, Storm }

        static Sky Classify(int code) => code switch
        {
            0 or 1 => Sky.Clear,
            2 => Sky.Partly,
            3 => Sky.Cloudy,
            45 or 48 => Sky.Fog,
            >= 51 and <= 67 => Sky.Rain,
            >= 80 and <= 82 => Sky.Rain,
            >= 71 and <= 77 => Sky.Snow,
            85 or 86 => Sky.Snow,
            >= 95 => Sky.Storm,
            _ => Sky.Cloudy
        };

        public static string Temp(double t) => Math.Round(t).ToString("0", Inv) + "°";

        // ---------------- vektor ikonlar (24x24, cizgi) ----------------
        const string CloudPath = "M7,18 L17,18 A4,4 0 0 0 17.5,10 A5.5,5.5 0 0 0 7,9.5 A4.25,4.25 0 0 0 7,18 Z";

        /// <summary>Hava ikonu; renk verilen firca ile cizilir (koyu/acik temaya uyar).</summary>
        public static FrameworkElement Icon(int code, bool day, double size, Brush stroke)
        {
            var c = new Canvas { Width = 24, Height = 24 };
            var sky = Classify(code);
            switch (sky)
            {
                case Sky.Clear:
                    c.Children.Add(day ? Sun(12, 12, 4.2, 7, 9.5, stroke) : Moon(stroke));
                    break;
                case Sky.Partly:
                    {
                        var body = day ? Sun(9, 9, 3.4, 5.6, 7.6, stroke) : Moon(stroke, 0.72, -1, -1);
                        // bulutun arkasinda kalan kismi kes: cizgiler ust uste binmesin
                        var cloud = Geometry.Parse(CloudPath).Clone();
                        cloud.Transform = new TransformGroup { Children = { new ScaleTransform(0.82, 0.82), new TranslateTransform(5.2, 4.6) } };
                        var hide = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-4, -4, 32, 32)), cloud);
                        body.Clip = hide;
                        c.Children.Add(body);
                        c.Children.Add(Stroke(cloud, stroke));
                        break;
                    }
                case Sky.Cloudy:
                    c.Children.Add(Stroke(Geometry.Parse(CloudPath), stroke));
                    break;
                case Sky.Fog:
                    c.Children.Add(Stroke(Geometry.Parse("M4,8 L20,8 M6,12 L18,12 M4,16 L20,16 M8,20 L16,20"), stroke));
                    break;
                default:
                    {
                        var cloud = Geometry.Parse(CloudPath).Clone();
                        cloud.Transform = new TranslateTransform(0, -3);
                        c.Children.Add(Stroke(cloud, stroke));
                        if (sky == Sky.Rain) c.Children.Add(Stroke(Geometry.Parse("M9,18 L8,21.5 M13,18 L12,21.5 M17,18 L16,21.5"), stroke));
                        else if (sky == Sky.Storm) c.Children.Add(Stroke(Geometry.Parse("M13.2,16 L10.6,19.6 L13.6,19.6 L11,23"), stroke));
                        else
                            foreach (var (x, y) in new[] { (8.5, 19.0), (12.5, 21.5), (16.5, 19.0) })
                            {
                                var dot = new Ellipse { Width = 2.4, Height = 2.4, Fill = stroke };
                                Canvas.SetLeft(dot, x - 1.2); Canvas.SetTop(dot, y - 1.2);
                                c.Children.Add(dot);
                            }
                        break;
                    }
            }
            return new Viewbox { Width = size, Height = size, Child = c, IsHitTestVisible = false };
        }

        static Path Stroke(Geometry g, Brush b) => new Path
        {
            Data = g, Stroke = b, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };

        static Path Sun(double cx, double cy, double r, double r1, double r2, Brush b)
        {
            var sb = new StringBuilder();
            sb.Append(FormattableString.Invariant($"M{cx - r},{cy} A{r},{r} 0 1 0 {cx + r},{cy} A{r},{r} 0 1 0 {cx - r},{cy} Z "));
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                sb.Append(FormattableString.Invariant($"M{cx + r1 * Math.Cos(a):0.##},{cy + r1 * Math.Sin(a):0.##} L{cx + r2 * Math.Cos(a):0.##},{cy + r2 * Math.Sin(a):0.##} "));
            }
            return Stroke(Geometry.Parse(sb.ToString()), b);
        }

        static Path Moon(Brush b, double scale = 1, double dx = 0, double dy = 0)
        {
            var g = Geometry.Parse("M14,3 A9,9 0 1 0 21,14 A7,7 0 0 1 14,3 Z").Clone();
            if (scale != 1 || dx != 0 || dy != 0)
                g.Transform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(dx, dy) } };
            return Stroke(g, b);
        }
    }
}
