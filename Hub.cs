using System;
using System.Collections.Generic;
using System.Linq;

namespace FloatingDock
{
    class NotificationEntry
    {
        public NotificationInfo Info;
        public string AppName, IconPath;
        public DateTime Time;
    }

    /// <summary>Pencereler arasi paylasilan durum: medya oturumu ve bildirim gecmisi.</summary>
    static class Hub
    {
        // ---------- medya ----------
        public static readonly MediaWatcher Media = new MediaWatcher();
        public static MediaInfo CurrentMedia { get; private set; }
        /// <summary>Arka plan is parcacigindan cagrilabilir; dinleyen Dispatcher'a gecmeli.</summary>
        public static event Action<MediaInfo> MediaChanged;

        static Hub() => Media.Changed += m => { CurrentMedia = m; MediaChanged?.Invoke(m); };

        // ---------- bildirim gecmisi (yalniz bellekte; diske yazilmaz) ----------
        static readonly List<NotificationEntry> history = new List<NotificationEntry>();
        public static IReadOnlyList<NotificationEntry> History => history;
        public static event Action HistoryChanged;
        public static int Unread { get; private set; }

        public static void AddNotification(NotificationEntry e)
        {
            history.Insert(0, e);
            if (history.Count > 30) history.RemoveRange(30, history.Count - 30);
            Unread++;
            HistoryChanged?.Invoke();
        }

        /// <summary>FloatingDock'un kendi bildirimi: adada gosterilir, gecmise yazilir.</summary>
        public static event Action<NotificationInfo> LocalToast;
        public static void Toast(string title, string body, Action click = null) =>
            LocalToast?.Invoke(new NotificationInfo { Title = title, Body = body ?? "", Source = "FloatingDock", Click = click });

        public static void Remove(NotificationEntry e) { history.Remove(e); HistoryChanged?.Invoke(); }
        public static void ClearHistory() { history.Clear(); Unread = 0; HistoryChanged?.Invoke(); }
        public static void MarkRead() { if (Unread == 0) return; Unread = 0; HistoryChanged?.Invoke(); }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return L.T("şimdi", "now");
            if (d.TotalHours < 1) return (int)d.TotalMinutes + L.T(" dk önce", " min ago");
            if (d.TotalDays < 1) return (int)d.TotalHours + L.T(" saat önce", " h ago");
            return t.ToString(L.En ? "MMM d HH:mm" : "d MMM HH:mm", L.Culture);
        }
    }
}
