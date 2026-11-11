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
        public string Title = "", Artist = "", Album = "", SourceId = "";
        public byte[] Art;
        public bool Playing;
        public TimeSpan Position, Duration;
        public DateTimeOffset Updated;
        /// <summary>Karistirma acik mi (null: uygulama bildirmiyor).</summary>
        public bool? Shuffle;
        /// <summary>0 kapali, 1 tek parca, 2 liste; -1 bilinmiyor.</summary>
        public int Repeat = -1;
        public bool CanSeek, CanShuffle, CanRepeat, CanPrev = true, CanNext = true;

        /// <summary>Su anki konum: oynarken son guncellemeden bu yana gecen sure eklenir.</summary>
        public TimeSpan LivePosition
        {
            get
            {
                var p = Position;
                if (Playing && Updated != default) p += DateTimeOffset.Now - Updated;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }

        public bool IsSpotify => SourceId.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Kaynak uygulamanin surec adi (ses kaydiricisi icin): "Spotify.exe" -> "spotify". Bilinmiyorsa null.</summary>
        public string ProcessName
        {
            get
            {
                if (IsSpotify) return "spotify";
                if (SourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetFileNameWithoutExtension(SourceId).ToLowerInvariant();
                return null;
            }
        }
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
                var ctl = pb?.Controls;
                var info = new MediaInfo
                {
                    Title = p?.Title ?? "",
                    Artist = p?.Artist ?? "",
                    Album = p?.AlbumTitle ?? "",
                    Shuffle = pb?.IsShuffleActive,
                    Repeat = pb?.AutoRepeatMode == null ? -1 : pb.AutoRepeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? 1
                           : pb.AutoRepeatMode == Windows.Media.MediaPlaybackAutoRepeatMode.List ? 2 : 0,
                    CanSeek = ctl?.IsPlaybackPositionEnabled == true && tl.EndTime > tl.StartTime,
                    CanShuffle = ctl?.IsShuffleEnabled == true,
                    CanRepeat = ctl?.IsRepeatEnabled == true,
                    CanPrev = ctl == null || ctl.IsPreviousEnabled,
                    CanNext = ctl == null || ctl.IsNextEnabled,
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

        /// <summary>Parcada verilen konuma atla (uygulama destekliyorsa).</summary>
        public void SeekTo(TimeSpan pos)
        {
            var s = shown;
            if (s == null) return;
            try
            {
                var start = s.GetTimelineProperties().StartTime;
                _ = s.TryChangePlaybackPositionAsync((start + pos).Ticks).AsTask().ContinueWith(_ => UpdateAsync());
            }
            catch { }
        }

        public void ToggleShuffle()
        {
            var s = shown;
            if (s == null) return;
            try { bool on = s.GetPlaybackInfo()?.IsShuffleActive == true; _ = s.TryChangeShuffleActiveAsync(!on).AsTask(); } catch { }
        }

        /// <summary>Tekrarla: kapali -> liste -> tek parca -> kapali.</summary>
        public void CycleRepeat()
        {
            var s = shown;
            if (s == null) return;
            try
            {
                var m = s.GetPlaybackInfo()?.AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;
                var next = m == Windows.Media.MediaPlaybackAutoRepeatMode.None ? Windows.Media.MediaPlaybackAutoRepeatMode.List
                         : m == Windows.Media.MediaPlaybackAutoRepeatMode.List ? Windows.Media.MediaPlaybackAutoRepeatMode.Track
                         : Windows.Media.MediaPlaybackAutoRepeatMode.None;
                _ = s.TryChangeAutoRepeatModeAsync(next).AsTask();
            }
            catch { }
        }
    }
}
