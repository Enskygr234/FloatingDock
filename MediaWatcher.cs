using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace FloatingDock
{
    class MediaInfo
    {
        public string Title = "", Artist = "", SourceId = "";
        public byte[] Art;
        public bool Playing;
        public TimeSpan Position, Duration;
        public DateTimeOffset Updated;
    }

    /// <summary>Windows medya oturumlarini (Spotify, tarayicilar vb.) izler.</summary>
    class MediaWatcher
    {
        GlobalSystemMediaTransportControlsSessionManager mgr;
        readonly List<GlobalSystemMediaTransportControlsSession> hooked = new List<GlobalSystemMediaTransportControlsSession>();
        GlobalSystemMediaTransportControlsSession shown;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        string artKey;
        byte[] art;
        public event Action<MediaInfo> Changed;

        public async Task StartAsync()
        {
            mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.SessionsChanged += (s, e) => { Rehook(); _ = UpdateAsync(); };
            mgr.CurrentSessionChanged += (s, e) => _ = UpdateAsync();
            Rehook();
            await UpdateAsync();
        }

        void Rehook()
        {
            lock (hooked)
            {
                foreach (var s in hooked)
                {
                    s.MediaPropertiesChanged -= OnMedia;
                    s.PlaybackInfoChanged -= OnPlayback;
                    s.TimelinePropertiesChanged -= OnTimeline;
                }
                hooked.Clear();
                foreach (var s in mgr.GetSessions())
                {
                    s.MediaPropertiesChanged += OnMedia;
                    s.PlaybackInfoChanged += OnPlayback;
                    s.TimelinePropertiesChanged += OnTimeline;
                    hooked.Add(s);
                }
            }
        }

        void OnMedia(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = UpdateAsync();
        void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => _ = UpdateAsync();
        void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => _ = UpdateAsync();

        GlobalSystemMediaTransportControlsSession Pick()
        {
            var sessions = mgr.GetSessions();
            var playing = sessions.FirstOrDefault(s =>
            {
                try { return s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
                catch { return false; }
            });
            if (playing != null) return playing;
            if (shown != null)
            {
                var same = sessions.FirstOrDefault(s => s.SourceAppUserModelId == shown.SourceAppUserModelId);
                if (same != null) return same;
            }
            return mgr.GetCurrentSession();
        }

        async Task UpdateAsync()
        {
            await gate.WaitAsync();
            try
            {
                var s = Pick();
                shown = s;
                if (s == null) { Changed?.Invoke(null); return; }
                var p = await s.TryGetMediaPropertiesAsync();
                var pb = s.GetPlaybackInfo();
                var tl = s.GetTimelineProperties();
                var info = new MediaInfo
                {
                    Title = p?.Title ?? "",
                    Artist = p?.Artist ?? "",
                    SourceId = s.SourceAppUserModelId ?? "",
                    Playing = pb != null && pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    Position = tl.Position,
                    Duration = tl.EndTime - tl.StartTime,
                    Updated = tl.LastUpdatedTime
                };
                if (string.IsNullOrWhiteSpace(info.Title)) { Changed?.Invoke(null); return; }
                var key = info.SourceId + "|" + info.Title + "|" + info.Artist;
                if (key != artKey || art == null)
                {
                    art = await ReadThumb(p?.Thumbnail);
                    artKey = key;
                }
                info.Art = art;
                Changed?.Invoke(info);
            }
            catch { }
            finally { gate.Release(); }
        }

        static async Task<byte[]> ReadThumb(IRandomAccessStreamReference r)
        {
            if (r == null) return null;
            try
            {
                using var st = await r.OpenReadAsync();
                using var ms = new MemoryStream();
                await st.AsStreamForRead().CopyToAsync(ms);
                return ms.Length > 0 ? ms.ToArray() : null;
            }
            catch { return null; }
        }

        public void PlayPause() { var s = shown; if (s != null) _ = s.TryTogglePlayPauseAsync().AsTask(); }
        public void Next() { var s = shown; if (s != null) _ = s.TrySkipNextAsync().AsTask(); }
        public void Prev() { var s = shown; if (s != null) _ = s.TrySkipPreviousAsync().AsTask(); }
    }
}
