using System;
using System.Globalization;

namespace FloatingDock
{
    /// <summary>
    /// Arayuz dili. Metinler kodun icinde iki dilde yazilir: L.T("Türkçe", "English").
    /// Ayar "system" ise Windows dili Turkce degilse Ingilizce kullanilir.
    /// </summary>
    static class L
    {
        /// <summary>Ingilizce mi?</summary>
        public static readonly bool En = Resolve();

        /// <summary>Tarih/saat bicimleri ve siralama icin kultur.</summary>
        public static readonly CultureInfo Culture = new CultureInfo(En ? "en-US" : "tr-TR");

        static bool Resolve()
        {
            try
            {
                var lang = Settings.Current.Language;
                if (lang == "en") return true;
                if (lang == "tr") return false;
                return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "tr";
            }
            catch { return false; }
        }

        public static string T(string tr, string en) => En ? en : tr;

        /// <summary>Kulturune gore ilk harfi buyuk yazar (Turkcede i -> İ).</summary>
        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Culture) + s.Substring(1);
    }
}
