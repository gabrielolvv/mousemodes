using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>Janela invisível que recebe um atalho global (funciona mesmo com o jogo em foco).</summary>
    public sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int HotkeyId = 0x4D4D; // "MM"

        /// <summary>Atalhos que o usuário pode escolher. A ordem é a de tentativa quando um está ocupado.</summary>
        public static readonly string[][] Choices =
        {
            new[] { "CTRL", "ALT", "M" },
            new[] { "CTRL", "SHIFT", "M" },
            new[] { "CTRL", "ALT", "F12" },
            new[] { "CTRL", "SHIFT", "F12" }
        };

        private static readonly uint[] ChoiceMods =
        {
            MOD_CONTROL | MOD_ALT,
            MOD_CONTROL | MOD_SHIFT,
            MOD_CONTROL | MOD_ALT,
            MOD_CONTROL | MOD_SHIFT
        };

        private static readonly Keys[] ChoiceKeys = { Keys.M, Keys.M, Keys.F12, Keys.F12 };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private bool registered;

        public event Action Pressed;

        /// <summary>Teclas do atalho registrado agora (ex.: "CTRL", "ALT", "M"), ou null se nenhum.</summary>
        public string[] ActiveCaps { get; private set; }

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public static string Describe(int index)
        {
            return index >= 0 && index < Choices.Length ? string.Join(" + ", Choices[index]) : L.T("nenhum", "none");
        }

        /// <summary>O atalho que a pessoa escolheu: o livre (CustomHotkey), se válido, ou uma das opções fixas.</summary>
        public static string DescribePreference(int preferred, string custom)
        {
            uint mods;
            Keys key;
            if (TryParse(custom, out mods, out key)) return Describe(mods, key);
            return Describe(preferred >= 0 && preferred < Choices.Length ? preferred : 0);
        }

        /// <summary>
        /// Registra o atalho preferido (o livre, se houver; senão a opção fixa); se outro programa já usa,
        /// tenta as opções fixas. Retorna true se ficou o preferido.
        /// </summary>
        public bool Register(int preferred, string custom)
        {
            Unregister();
            if (preferred < 0 || preferred >= Choices.Length) preferred = 0;

            var mods = new List<uint>();
            var keys = new List<Keys>();
            uint customMods;
            Keys customKey;
            if (TryParse(custom, out customMods, out customKey))
            {
                mods.Add(customMods);
                keys.Add(customKey);
            }
            for (int n = 0; n < Choices.Length; n++)
            {
                int i = (preferred + n) % Choices.Length;
                if (mods.Count > 0 && ChoiceMods[i] == mods[0] && ChoiceKeys[i] == keys[0]) continue; // o livre já é esta
                mods.Add(ChoiceMods[i]);
                keys.Add(ChoiceKeys[i]);
            }

            string wanted = Describe(mods[0], keys[0]);
            for (int i = 0; i < mods.Count; i++)
            {
                if (!RegisterHotKey(Handle, HotkeyId, mods[i] | MOD_NOREPEAT, (uint)keys[i])) continue;
                registered = true;
                ActiveCaps = Caps(mods[i], keys[i]);
                if (i == 0) Log.Info("Atalho global: " + wanted);
                else Log.Warn(wanted + " já é usado por outro programa; usando " + Describe(mods[i], keys[i]));
                return i == 0;
            }
            Log.Warn("Nenhum atalho global disponível (todos usados por outros programas)");
            return false;
        }

        // ---- Atalho livre: "Ctrl+Alt+K" no config, "CTRL + ALT + K" na tela ----

        /// <summary>Modificadores e tecla de uma das opções fixas.</summary>
        public static void Choice(int index, out uint mods, out Keys key)
        {
            if (index < 0 || index >= Choices.Length) index = 0;
            mods = ChoiceMods[index];
            key = ChoiceKeys[index];
        }

        /// <summary>Índice da opção fixa igual a este atalho, ou -1.</summary>
        public static int ChoiceIndex(uint mods, Keys key)
        {
            for (int i = 0; i < Choices.Length; i++)
                if (ChoiceMods[i] == mods && ChoiceKeys[i] == key) return i;
            return -1;
        }

        /// <summary>Modificadores do WinForms (Keys.Control etc.) para os do RegisterHotKey.</summary>
        public static uint ModsFrom(Keys modifiers)
        {
            uint mods = 0;
            if ((modifiers & Keys.Control) != 0) mods |= MOD_CONTROL;
            if ((modifiers & Keys.Alt) != 0) mods |= MOD_ALT;
            if ((modifiers & Keys.Shift) != 0) mods |= MOD_SHIFT;
            return mods;
        }

        /// <summary>Null se o atalho serve; senão, o motivo (para mostrar na tela).</summary>
        public static string Validate(uint mods, Keys key)
        {
            if ((mods & (MOD_CONTROL | MOD_ALT)) == 0) return L.T("Use Ctrl ou Alt junto com outra tecla", "Use Ctrl or Alt together with another key");
            if (key == Keys.None || IsModifier(key)) return L.T("Falta a tecla além de Ctrl, Alt e Shift", "Missing a key besides Ctrl, Alt and Shift");
            if (key == Keys.Escape || key == Keys.Tab || key == Keys.LWin || key == Keys.RWin || key == Keys.Apps)
                return L.T("Esta tecla é do Windows; escolha outra", "That key belongs to Windows; pick another");
            return null;
        }

        internal static bool IsModifier(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                   key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                   key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
        }

        /// <summary>Formato do config: "Ctrl+Alt+K".</summary>
        public static string Format(uint mods, Keys key)
        {
            var parts = new List<string>();
            if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
            parts.Add(key.ToString());
            return string.Join("+", parts.ToArray());
        }

        /// <summary>Lê o formato do config. False se vazio, inválido ou não passar em Validate.</summary>
        public static bool TryParse(string text, out uint mods, out Keys key)
        {
            mods = 0;
            key = Keys.None;
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
                else
                {
                    Keys parsed;
                    // Só nomes de tecla: um número solto ("65") viraria uma tecla qualquer.
                    if (key != Keys.None || part.Length == 0 || char.IsDigit(part[0]) || part.IndexOf(',') >= 0 ||
                        !Enum.TryParse(part, true, out parsed) || (parsed & Keys.Modifiers) != 0) return false;
                    key = parsed;
                }
            }
            return Validate(mods, key) == null;
        }

        /// <summary>Teclas para desenhar: "CTRL", "ALT", "SHIFT" e a tecla ("K", "5", "F12", "NUM 1").</summary>
        public static string[] Caps(uint mods, Keys key)
        {
            var caps = new List<string>();
            if ((mods & MOD_CONTROL) != 0) caps.Add("CTRL");
            if ((mods & MOD_ALT) != 0) caps.Add("ALT");
            if ((mods & MOD_SHIFT) != 0) caps.Add("SHIFT");
            caps.Add(KeyName(key));
            return caps.ToArray();
        }

        public static string Describe(uint mods, Keys key)
        {
            return string.Join(" + ", Caps(mods, key));
        }

        internal static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "NUM " + (int)(key - Keys.NumPad0);
            switch (key)
            {
                case Keys.Space: return L.T("ESPAÇO", "SPACE");
                case Keys.Return: return "ENTER";
                case Keys.Back: return "BACKSPACE";
                case Keys.Delete: return "DEL";
                case Keys.Insert: return "INS";
                case Keys.PageUp: return "PG UP";
                case Keys.PageDown: return "PG DN";
                case Keys.Oemplus: return "=";
                case Keys.OemMinus: return "-";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                default: return key.ToString().ToUpperInvariant();
            }
        }

        private void Unregister()
        {
            if (!registered) return;
            UnregisterHotKey(Handle, HotkeyId);
            registered = false;
            ActiveCaps = null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                var handler = Pressed;
                if (handler != null) handler();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
