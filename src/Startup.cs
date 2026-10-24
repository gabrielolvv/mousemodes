using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseModes
{
    /// <summary>Iniciar com o Windows (HKCU\...\Run, não precisa de admin).</summary>
    public static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MouseModes";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return key != null && key.GetValue(ValueName) != null;
            }
        }

        private static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\" --tray"; }
        }

        /// <summary>Se estiver ligado mas apontando para outro lugar (exe movido ou instalado), aponta para este exe.</summary>
        public static void RefreshPath()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                string current = key.GetValue(ValueName) as string;
                if (current == null || string.Equals(current, Command, System.StringComparison.OrdinalIgnoreCase)) return;
                // Se o exe registrado ainda existe (ex.: a versão instalada), não troca por uma cópia solta.
                string registered = current.Trim();
                if (registered.StartsWith("\""))
                {
                    int end = registered.IndexOf('"', 1);
                    if (end > 1) registered = registered.Substring(1, end - 1);
                }
                if (System.IO.File.Exists(registered)) return;
                key.SetValue(ValueName, Command);
                Log.Info("Iniciar com o Windows atualizado para " + Application.ExecutablePath);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) return;
                if (enabled)
                    key.SetValue(ValueName, Command);
                else if (key.GetValue(ValueName) != null)
                    key.DeleteValue(ValueName, false);
            }
        }
    }
}
