using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace FloatingDock
{
    class WifiNetwork
    {
        public string Ssid = "", Profile = "";
        public byte[] SsidBytes = Array.Empty<byte>();
        public int Signal;          // 0-100
        public bool Secured, Connected, HasProfile;
        public int Auth, Cipher;    // DOT11_AUTH_ALGORITHM / DOT11_CIPHER_ALGORITHM
        public bool Supported => !Secured || Auth == 1 || Auth == 4 || Auth == 7 || Auth == 9;
    }

    /// <summary>Windows'un yerel Wi-Fi arayuzu (wlanapi.dll): agları listele, baglan, baglantiyi kes.</summary>
    static class Wlan
    {
        // ---------- P/Invoke ----------
        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint ver, IntPtr res, out uint neg, out IntPtr h);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr h, IntPtr res);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr h, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr h, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr res);
        [DllImport("wlanapi.dll")] static extern int WlanGetAvailableNetworkList(IntPtr h, ref Guid iface, uint flags, IntPtr res, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanConnect(IntPtr h, ref Guid iface, ref CONN p, IntPtr res);
        [DllImport("wlanapi.dll")] static extern int WlanDisconnect(IntPtr h, ref Guid iface, IntPtr res);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        static extern int WlanSetProfile(IntPtr h, ref Guid iface, uint flags, string xml, string sec, bool overwrite, IntPtr res, out uint reason);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        static extern int WlanDeleteProfile(IntPtr h, ref Guid iface, string name, IntPtr res);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NET
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strProfileName;
            public uint ssidLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] ssid;
            public int bssType;
            public uint numBssids;
            public int connectable;
            public uint notConnectableReason;
            public uint numPhyTypes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public int[] phyTypes;
            public int morePhyTypes;
            public uint signalQuality;
            public int securityEnabled;
            public int auth;
            public int cipher;
            public uint flags;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CONN
        {
            public int mode;              // 0 = wlan_connection_mode_profile
            [MarshalAs(UnmanagedType.LPWStr)] public string profile;
            public IntPtr ssid;
            public IntPtr bssidList;
            public int bssType;           // 1 = infrastructure
            public uint flags;
        }

        // ---------- yardimcilar ----------
        static T With<T>(Func<IntPtr, Guid, T> f, T fallback)
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return fallback;
            try
            {
                var g = FirstInterface(h);
                return g == null ? fallback : f(h, g.Value);
            }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        static Guid? FirstInterface(IntPtr h)
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out var list) != 0) return null;
            try
            {
                int n = Marshal.ReadInt32(list);
                if (n == 0) return null;
                var b = new byte[16];
                Marshal.Copy(list + 8, b, 0, 16);
                return new Guid(b);
            }
            finally { WlanFreeMemory(list); }
        }

        public static bool HasAdapter => With((h, g) => true, false);

        public static void Scan() => With((h, g) => WlanScan(h, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero), 0);

        public static List<WifiNetwork> List()
        {
            return With((h, g) =>
            {
                var res = new List<WifiNetwork>();
                if (WlanGetAvailableNetworkList(h, ref g, 0, IntPtr.Zero, out var list) != 0) return res;
                try
                {
                    int n = Marshal.ReadInt32(list);
                    int size = Marshal.SizeOf<NET>();
                    for (int i = 0; i < n; i++)
                    {
                        var x = Marshal.PtrToStructure<NET>(list + 8 + i * size);
                        if (x.ssidLen == 0) continue; // gizli ag
                        var bytes = x.ssid.Take((int)Math.Min(32, x.ssidLen)).ToArray();
                        res.Add(new WifiNetwork
                        {
                            Ssid = Encoding.UTF8.GetString(bytes),
                            SsidBytes = bytes,
                            Profile = x.strProfileName ?? "",
                            Signal = (int)x.signalQuality,
                            Secured = x.securityEnabled != 0,
                            Connected = (x.flags & 1) != 0,
                            HasProfile = (x.flags & 2) != 0,
                            Auth = x.auth,
                            Cipher = x.cipher
                        });
                    }
                }
                finally { WlanFreeMemory(list); }
                // ayni SSID birden fazla gelebilir (profilli + profilsiz): baglı/profilli olani tut
                return res.GroupBy(w => w.Ssid)
                          .Select(gr => gr.OrderByDescending(w => w.Connected).ThenByDescending(w => w.HasProfile).ThenByDescending(w => w.Signal).First())
                          .OrderByDescending(w => w.Connected).ThenByDescending(w => w.Signal)
                          .ToList();
            }, new List<WifiNetwork>());
        }

        public static void Disconnect() => With((h, g) => WlanDisconnect(h, ref g, IntPtr.Zero), 0);

        /// <summary>Kayitli profil varsa onunla, yoksa (sifre ile) yeni profil olusturup baglanir. Basarili olursa true.</summary>
        public static async Task<bool> ConnectAsync(WifiNetwork net, string password)
        {
            bool created = false;
            string profile = net.HasProfile && !string.IsNullOrEmpty(net.Profile) ? net.Profile : null;
            if (profile == null)
            {
                var xml = ProfileXml(net, password);
                if (xml == null) return false;
                int r = With((h, g) => WlanSetProfile(h, ref g, 0, xml, null, true, IntPtr.Zero, out _), -1);
                if (r != 0) return false;
                profile = net.Ssid;
                created = true;
            }
            int rc = With((h, g) =>
            {
                var p = new CONN { mode = 0, profile = profile, bssType = 1 };
                return WlanConnect(h, ref g, ref p, IntPtr.Zero);
            }, -1);

            bool ok = false;
            if (rc == 0)
            {
                for (int i = 0; i < 40 && !ok; i++) // ~16 sn bekle
                {
                    await Task.Delay(400);
                    ok = await Task.Run(() => List().Any(w => w.Ssid == net.Ssid && w.Connected));
                }
            }
            // yanlis sifre: olusturdugumuz profili geri sil ki Windows tekrar tekrar denemesin
            if (!ok && created) With((h, g) => WlanDeleteProfile(h, ref g, profile, IntPtr.Zero), 0);
            return ok;
        }

        public static void Forget(WifiNetwork net)
        {
            if (string.IsNullOrEmpty(net.Profile)) return;
            With((h, g) => WlanDeleteProfile(h, ref g, net.Profile, IntPtr.Zero), 0);
        }

        static string ProfileXml(WifiNetwork n, string password)
        {
            string auth, enc;
            switch (n.Secured ? n.Auth : 1)
            {
                case 1: auth = "open"; enc = "none"; break;
                case 4: auth = "WPAPSK"; enc = n.Cipher == 2 ? "TKIP" : "AES"; break;
                case 7: auth = "WPA2PSK"; enc = n.Cipher == 2 ? "TKIP" : "AES"; break;
                case 9: auth = "WPA3SAE"; enc = "AES"; break;
                default: return null; // kurumsal (802.1X) aglar desteklenmiyor
            }
            var hex = string.Concat(n.SsidBytes.Select(b => b.ToString("X2")));
            var name = SecurityElement.Escape(n.Ssid);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
            sb.Append($"<name>{name}</name><SSIDConfig><SSID><hex>{hex}</hex><name>{name}</name></SSID></SSIDConfig>");
            sb.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>");
            sb.Append($"<authEncryption><authentication>{auth}</authentication><encryption>{enc}</encryption><useOneX>false</useOneX></authEncryption>");
            if (auth != "open")
                sb.Append($"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password ?? "")}</keyMaterial></sharedKey>");
            sb.Append("</security></MSM></WLANProfile>");
            return sb.ToString();
        }
    }
}
