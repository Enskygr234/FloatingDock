using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace FloatingDock
{
    class NotificationInfo
    {
        public string AppId = "", Title = "", Body = "";
        // tepsi balonu / zamanlayici gibi Windows veritabanindan gelmeyen bildirimler icin
        public string AppName, Glyph;
        public System.Windows.Media.ImageSource Image;
        public Action OnClick;
    }

    /// <summary>Windows bildirim veritabanini (sadece okuma) yoklayip yeni bildirimleri bulur.</summary>
    class NotificationWatcher
    {
        static readonly string Db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\Notifications\wpndatabase.db");
        long lastId = -1;
        public string LastError;

        public List<NotificationInfo> Poll()
        {
            var result = new List<NotificationInfo>();
            try
            {
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Mode = SqliteOpenMode.ReadOnly }.ToString());
                c.Open();
                using var cmd = c.CreateCommand();
                if (lastId < 0)
                {
                    cmd.CommandText = "SELECT IFNULL(MAX(Id),0) FROM Notification";
                    lastId = Convert.ToInt64(cmd.ExecuteScalar());
                    return result;
                }
                cmd.CommandText = "SELECT n.Id, n.Payload, h.PrimaryId FROM Notification n " +
                                  "LEFT JOIN NotificationHandler h ON n.HandlerId = h.RecordId " +
                                  "WHERE n.Id > $id AND n.Type = 'toast' ORDER BY n.Id";
                cmd.Parameters.AddWithValue("$id", lastId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    lastId = Math.Max(lastId, r.GetInt64(0));
                    var raw = r.IsDBNull(1) ? null : r.GetValue(1);
                    string xml = raw is byte[] b ? Encoding.UTF8.GetString(b) : raw as string;
                    var n = Parse(xml);
                    if (n == null) continue;
                    n.AppId = r.IsDBNull(2) ? "" : r.GetString(2);
                    result.Add(n);
                }
                LastError = null;
            }
            catch (Exception ex) { LastError = ex.Message; }
            return result;
        }

        static NotificationInfo Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try
            {
                var doc = XDocument.Parse(xml.TrimEnd('\0'));
                var texts = doc.Descendants().Where(e => e.Name.LocalName == "text")
                    .Where(e => (string)e.Attribute("placement") != "attribution")
                    .Select(e => e.Value.Trim()).Where(t => t.Length > 0).ToList();
                if (texts.Count == 0) return null;
                return new NotificationInfo { Title = texts[0], Body = string.Join(" · ", texts.Skip(1)) };
            }
            catch { return null; }
        }
    }
}
