using System;
using Microsoft.Win32;

namespace FloatingDock
{
    /// <summary>Kullanici kendi istediginde: HKCU\...\Run ile Windows acilisinda baslatma.</summary>
    static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "FloatingDock";

        public static bool IsEnabled
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(Key);
                return k?.GetValue(Name) is string v && v.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void Set(bool on)
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (on) k.SetValue(Name, "\"" + Environment.ProcessPath + "\"");
            else k.DeleteValue(Name, false);
        }
    }
}
