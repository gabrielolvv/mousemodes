using System;
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

        /// <summary>Índice em Choices do atalho registrado agora, ou -1.</summary>
        public int Active { get; private set; }

        public HotkeyWindow()
        {
            Active = -1;
            CreateHandle(new CreateParams());
        }

        public static string Describe(int index)
        {
            return index >= 0 && index < Choices.Length ? string.Join(" + ", Choices[index]) : "nenhum";
        }

        /// <summary>
        /// Registra o atalho preferido; se outro programa já usa, tenta os demais.
        /// Retorna o índice registrado ou -1.
        /// </summary>
        public int Register(int preferred)
        {
            Unregister();
            if (preferred < 0 || preferred >= Choices.Length) preferred = 0;

            for (int n = 0; n < Choices.Length; n++)
            {
                int i = (preferred + n) % Choices.Length;
                if (RegisterHotKey(Handle, HotkeyId, ChoiceMods[i] | MOD_NOREPEAT, (uint)ChoiceKeys[i]))
                {
                    registered = true;
                    Active = i;
                    if (i != preferred)
                        Log.Warn(Describe(preferred) + " já é usado por outro programa; usando " + Describe(i));
                    else
                        Log.Info("Atalho global: " + Describe(i));
                    return i;
                }
            }
            Log.Warn("Nenhum atalho global disponível (todos usados por outros programas)");
            return -1;
        }

        private void Unregister()
        {
            if (!registered) return;
            UnregisterHotKey(Handle, HotkeyId);
            registered = false;
            Active = -1;
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
