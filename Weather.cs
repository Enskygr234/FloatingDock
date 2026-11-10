using System;
using System.Collections.Generic;
using System.Globalization;
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
    class WeatherDay { public DateTime Date; public int Code; public double Max, Min; }

    class WeatherData
    {
        public double Temp, Feels, Wind;
        public int Code;
        public bool IsDay = true;
        public string Place;
        public List<WeatherDay> Days = new List<WeatherDay>();
        public DateTime Fetched;
    }

    /// <summary>
    /// Hava durumu (open-meteo.com, anahtarsiz). Yalniz ayarlarda sehir girildiyse calisir:
    /// sehir adi bir kez koordinata cevrilir, sonra her 20 dakikada bir tahmin alinir. Baska hicbir veri gonderilmez.
    /// </summary>
    static class Weather
    {
        static readonly HttpClient http = CreateClient();
        static System.Threading.Timer timer;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static WeatherData Current { get; private set; }
        public static string LastError { get; private set; }
        /// <summary>Arka plan is parcacigindan gelebilir; dinleyen Dispatcher'a gecmeli.</summary>
        public static event Action Changed;
        public static bool Enabled => !string.IsNullOrWhiteSpace(Settings.Current.WeatherCity);

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FloatingDock/1.3");
            return c;
        }

        public static void Start()
        {
            timer ??= new System.Threading.Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(20));
        }

        public static async Task<bool> RefreshAsync()
        {
            var s = Settings.Current;
            if (!Enabled)
            {
                if (Current != null) { Current = null; Changed?.Invoke(); }
                return false;
            }
            try
            {
                if (s.WeatherLat == 0 && s.WeatherLon == 0)
                {
                    var g = await GeocodeAsync(s.WeatherCity);
                    if (g == null) { LastError = "Şehir bulunamadı"; Changed?.Invoke(); return false; }
                    s.WeatherLat = g.Value.lat; s.WeatherLon = g.Value.lon; s.WeatherPlace = g.Value.name;
                    // Save -> Changed olayi arayuze dokunur: UI is parcaciginda kaydet
                    Application.Current?.Dispatcher.BeginInvoke(new Action(s.Save));
                }
                var url = "https://api.open-meteo.com/v1/forecast?latitude=" + s.WeatherLat.ToString(Inv) + "&longitude=" + s.WeatherLon.ToString(Inv) +
                          "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m" +
                          "&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=5";
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                var cur = doc.RootElement.GetProperty("current");
                var d = new WeatherData
                {
                    Temp = cur.GetProperty("temperature_2m").GetDouble(),
                    Feels = cur.GetProperty("apparent_temperature").GetDouble(),
                    Wind = cur.GetProperty("wind_speed_10m").GetDouble(),
                    Code = cur.GetProperty("weather_code").GetInt32(),
                    IsDay = cur.GetProperty("is_day").GetInt32() == 1,
                    Place = string.IsNullOrEmpty(s.WeatherPlace) ? s.WeatherCity : s.WeatherPlace,
                    Fetched = DateTime.Now
                };
                var daily = doc.RootElement.GetProperty("daily");
                var times = daily.GetProperty("time");
                var codes = daily.GetProperty("weather_code");
                var max = daily.GetProperty("temperature_2m_max");
                var min = daily.GetProperty("temperature_2m_min");
                for (int i = 0; i < times.GetArrayLength(); i++)
                {
                    if (codes[i].ValueKind != JsonValueKind.Number || max[i].ValueKind != JsonValueKind.Number) continue;
                    d.Days.Add(new WeatherDay
                    {
                        Date = DateTime.ParseExact(times[i].GetString(), "yyyy-MM-dd", Inv),
                        Code = codes[i].GetInt32(), Max = max[i].GetDouble(), Min = min[i].GetDouble()
                    });
                }
                Current = d;
                LastError = null;
                Changed?.Invoke();
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
        }

        /// <summary>Ayarlardan yeni sehir: bulunursa kaydeder ve hemen tahmini alir. Bulunamazsa null, bos ise kapatir.</summary>
        public static async Task<string> SetCityAsync(string city)
        {
            var s = Settings.Current;
            city = (city ?? "").Trim();
            if (city.Length == 0)
            {
                s.WeatherCity = ""; s.WeatherPlace = ""; s.WeatherLat = 0; s.WeatherLon = 0; s.Save();
                Current = null; LastError = null; Changed?.Invoke();
                return "";
            }
            var g = await GeocodeAsync(city);
            if (g == null) return null;
            s.WeatherCity = city; s.WeatherLat = g.Value.lat; s.WeatherLon = g.Value.lon; s.WeatherPlace = g.Value.name;
            s.Save();
            Start();
            await RefreshAsync();
            return g.Value.name;
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
