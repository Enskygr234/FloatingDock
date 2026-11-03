using System;
using System.Runtime.InteropServices;

namespace FloatingDock
{
    /// <summary>Varsayilan hoparlorun ses seviyesi ve sessiz durumu (Core Audio).</summary>
    static class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int c);
            [PreserveSig] int GetAt(int i, out PROPERTYKEY k);
            [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        }

        [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public int pid; }
        [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt; ushort r1, r2, r3; public IntPtr p; IntPtr p2; }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
            [PreserveSig] int GetChannelCount(out uint c);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static Guid ctx = Guid.Empty;

        static IMMDevice Device()
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                return en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var d) == 0 ? d : null;
            }
            catch { return null; }
        }

        static IAudioEndpointVolume Endpoint()
        {
            var d = Device();
            if (d == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            return d.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out var o) == 0 ? o as IAudioEndpointVolume : null;
        }

        public static bool Available => Endpoint() != null;

        /// <summary>0..1, cihaz yoksa -1</summary>
        public static float Volume
        {
            get { var e = Endpoint(); return e != null && e.GetMasterVolumeLevelScalar(out var v) == 0 ? v : -1; }
            set { Endpoint()?.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref ctx); }
        }

        public static bool Muted
        {
            get { var e = Endpoint(); return e != null && e.GetMute(out var m) == 0 && m; }
            set { Endpoint()?.SetMute(value, ref ctx); }
        }

        public static string DeviceName
        {
            get
            {
                try
                {
                    var d = Device();
                    if (d == null || d.OpenPropertyStore(0, out var ps) != 0) return null;
                    // PKEY_Device_FriendlyName
                    var k = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                    if (ps.GetValue(ref k, out var v) != 0 || v.vt != 31 /*VT_LPWSTR*/) return null;
                    return Marshal.PtrToStringUni(v.p);
                }
                catch { return null; }
            }
        }
    }
}
