// Настройки программы в реестре: HKCU\Software\ZapretGUI.
using Microsoft.Win32;

namespace ZapretGui
{
    static class Prefs
    {
        const string Key = @"Software\ZapretGUI";
        public static string Get(string name, string def)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { var v = k == null ? null : k.GetValue(name) as string; return v ?? def; } }
            catch { return def; }
        }
        public static void Set(string name, string value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value ?? ""); } catch { }
        }
    }
}
