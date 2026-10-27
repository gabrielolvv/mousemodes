using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace MouseModes
{
    /// <summary>
    /// Bloqueia a tecla Windows só quando um jogo da lista está em primeiro plano.
    /// O hook roda numa thread só dele: o Windows segura cada tecla do PC inteiro até o hook responder,
    /// então ele não pode dividir a thread com a tela (powercfg, varredura de processos etc.).
    /// </summary>
    public sealed class WinKeyBlocker : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WM_QUIT = 0x0012;
        private const uint WM_APP_RECHECK = 0x8000 + 1; // WM_APP + 1: a lista de jogos mudou
        private const uint PM_NOREMOVE = 0x0000;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // Referências guardadas para o GC não recolher os delegates.
        private readonly HookProc keyProc;
        private readonly WinEventProc foregroundProc;
        private readonly Func<IList<string>> games;

        private Thread thread;
        private uint threadId;

        // Escritos pela tela, lidos pela thread do hook.
        private volatile string[] gameList = new string[0];

        // Só a thread do hook mexe nestes.
        private IntPtr hook = IntPtr.Zero;
        private IntPtr foregroundHook = IntPtr.Zero;
        private uint cachedPid;
        private bool cachedIsGame;
        private int blockedCount;

        public WinKeyBlocker(Func<IList<string>> games)
        {
            this.games = games;
            keyProc = KeyCallback;
            foregroundProc = ForegroundChanged;
        }

        public bool Active { get { return thread != null; } }

        /// <summary>Liga o bloqueio, ou só atualiza a lista de jogos se já estiver ligado.</summary>
        public void Enable()
        {
            gameList = new List<string>(games()).ToArray();

            if (thread != null)
            {
                PostThreadMessage(threadId, WM_APP_RECHECK, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            bool installed = false;
            // Sem using: se a espera estourar, a thread ainda vai sinalizar este evento depois.
            var ready = new ManualResetEvent(false);
            var t = new Thread(() => Run(ready, ref installed)) { IsBackground = true, Name = "MouseModes WinKey" };
            t.Priority = ThreadPriority.Highest; // dorme quase sempre; quando acorda, cada tecla espera por ela
            t.Start();
            if (!ready.WaitOne(5000))
            {
                // Fica registrada mesmo assim, para o Disable conseguir encerrá-la se ela terminar de subir.
                Log.Warn("Bloqueio da tecla Windows não respondeu ao iniciar");
                thread = t;
                return;
            }
            if (installed) thread = t;
            else t.Join(1000);
        }

        public void Disable()
        {
            if (thread == null) return;
            PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(2000))
                Log.Warn("Thread do bloqueio da tecla Windows não encerrou em 2 s");
            thread = null;
            Log.Info("Tecla Windows liberada" + (blockedCount > 0 ? " (bloqueada " + blockedCount + "x nesta sessão)" : ""));
        }

        private void Run(ManualResetEvent ready, ref bool installed)
        {
            MSG msg;
            try
            {
                threadId = GetCurrentThreadId();
                PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_NOREMOVE); // cria a fila de mensagens antes do PostThreadMessage
                cachedPid = 0;
                blockedCount = 0;

                IntPtr module = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
                hook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, module, 0);
                if (hook == IntPtr.Zero)
                {
                    Log.Warn("Bloqueio da tecla Windows não ativado (erro " + Marshal.GetLastWin32Error() + ")");
                    return;
                }

                // Saber o jogo em foco quando a janela muda, e não a cada tecla.
                foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                    foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
                if (foregroundHook == IntPtr.Zero)
                    Log.Warn("Aviso de troca de janela indisponível; o jogo em foco será conferido a cada tecla Windows");
                Recheck();

                installed = true;
                Log.Info("Tecla Windows bloqueada enquanto um jogo da lista estiver em primeiro plano");
            }
            catch (Exception ex)
            {
                Log.Error("Bloqueio da tecla Windows", ex);
                return;
            }
            finally
            {
                if (!installed && hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hook);
                    hook = IntPtr.Zero;
                }
                ready.Set();
            }

            // O Windows chama o hook de dentro do GetMessage desta thread.
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_APP_RECHECK) Recheck();
            }

            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            UnhookWindowsHookEx(hook);
            foregroundHook = IntPtr.Zero;
            hook = IntPtr.Zero;
        }

        private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == VK_LWIN || vk == VK_RWIN)
                {
                    if (foregroundHook == IntPtr.Zero) UpdateForeground(GetForegroundWindow());
                    if (cachedIsGame)
                    {
                        blockedCount++;
                        return (IntPtr)1; // engole a tecla
                    }
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private void ForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            UpdateForeground(hwnd);
        }

        /// <summary>A lista de jogos mudou: reavalia a janela em foco do zero.</summary>
        private void Recheck()
        {
            cachedPid = 0;
            UpdateForeground(GetForegroundWindow());
        }

        private void UpdateForeground(IntPtr hwnd)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0)
                {
                    cachedPid = 0;
                    cachedIsGame = false;
                    return;
                }
                if (pid == cachedPid) return;
                cachedPid = pid;
                using (Process p = Process.GetProcessById((int)pid))
                    cachedIsGame = GameBooster.Matches(p.ProcessName, gameList);
            }
            catch
            {
                cachedIsGame = false;
            }
        }

        public void Dispose()
        {
            Disable();
        }
    }

    /// <summary>
    /// Desliga os atalhos de acessibilidade (Shift 5x = Teclas de Aderência, Shift direito 8 s = Teclas de Filtro,
    /// Num Lock 5 s = Teclas de Alternância) só nesta sessão do Windows. Se o app travar, um reinício desfaz.
    /// </summary>
    public static class AccessibilityHotkeys
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct STICKYKEYS { public uint cbSize; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOGGLEKEYS { public uint cbSize; public uint dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILTERKEYS
        {
            public uint cbSize;
            public uint dwFlags;
            public uint iWaitMSec;
            public uint iDelayMSec;
            public uint iRepeatMSec;
            public uint iBounceMSec;
        }

        private const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
        private const uint SPI_GETTOGGLEKEYS = 0x0034, SPI_SETTOGGLEKEYS = 0x0035;
        private const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;
        private const uint ON = 0x1, HOTKEYACTIVE = 0x4, CONFIRMHOTKEY = 0x8;

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        private static extern bool Spi(uint action, uint uiParam, ref STICKYKEYS pv, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        private static extern bool Spi(uint action, uint uiParam, ref TOGGLEKEYS pv, uint winIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        private static extern bool Spi(uint action, uint uiParam, ref FILTERKEYS pv, uint winIni);

        private static bool saved;
        private static uint stickyFlags, toggleFlags, filterFlags;

        public static bool Disabled { get { return saved; } }

        public static void Disable()
        {
            if (saved) return;
            try
            {
                var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
                var tk = new TOGGLEKEYS { cbSize = (uint)Marshal.SizeOf(typeof(TOGGLEKEYS)) };
                var fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
                if (!Spi(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0) ||
                    !Spi(SPI_GETTOGGLEKEYS, tk.cbSize, ref tk, 0) ||
                    !Spi(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0))
                {
                    Log.Warn("Não foi possível ler os atalhos de acessibilidade");
                    return;
                }
                stickyFlags = sk.dwFlags;
                toggleFlags = tk.dwFlags;
                filterFlags = fk.dwFlags;
                saved = true;

                // Só tira o atalho. Se o recurso estiver LIGADO de propósito, não mexe.
                if ((sk.dwFlags & ON) == 0) { sk.dwFlags &= ~(HOTKEYACTIVE | CONFIRMHOTKEY); Spi(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, 0); }
                if ((tk.dwFlags & ON) == 0) { tk.dwFlags &= ~(HOTKEYACTIVE | CONFIRMHOTKEY); Spi(SPI_SETTOGGLEKEYS, tk.cbSize, ref tk, 0); }
                if ((fk.dwFlags & ON) == 0) { fk.dwFlags &= ~(HOTKEYACTIVE | CONFIRMHOTKEY); Spi(SPI_SETFILTERKEYS, fk.cbSize, ref fk, 0); }
                Log.Info("Atalhos de acessibilidade (Shift 5x etc.) desligados durante o Performance");
            }
            catch (Exception ex)
            {
                Log.Error("Desligar atalhos de acessibilidade", ex);
            }
        }

        public static void Restore()
        {
            if (!saved) return;
            try
            {
                var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf(typeof(STICKYKEYS)) };
                var tk = new TOGGLEKEYS { cbSize = (uint)Marshal.SizeOf(typeof(TOGGLEKEYS)) };
                var fk = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
                Spi(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);
                Spi(SPI_GETTOGGLEKEYS, tk.cbSize, ref tk, 0);
                Spi(SPI_GETFILTERKEYS, fk.cbSize, ref fk, 0);
                sk.dwFlags = (sk.dwFlags & ~(HOTKEYACTIVE | CONFIRMHOTKEY)) | (stickyFlags & (HOTKEYACTIVE | CONFIRMHOTKEY));
                tk.dwFlags = (tk.dwFlags & ~(HOTKEYACTIVE | CONFIRMHOTKEY)) | (toggleFlags & (HOTKEYACTIVE | CONFIRMHOTKEY));
                fk.dwFlags = (fk.dwFlags & ~(HOTKEYACTIVE | CONFIRMHOTKEY)) | (filterFlags & (HOTKEYACTIVE | CONFIRMHOTKEY));
                Spi(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, 0);
                Spi(SPI_SETTOGGLEKEYS, tk.cbSize, ref tk, 0);
                Spi(SPI_SETFILTERKEYS, fk.cbSize, ref fk, 0);
                Log.Info("Atalhos de acessibilidade restaurados");
            }
            catch (Exception ex)
            {
                Log.Error("Restaurar atalhos de acessibilidade", ex);
            }
            saved = false;
        }
    }

    /// <summary>Guarda o estado original das notificações, para desfazer mesmo depois de um travamento.</summary>
    public class NotificationBackup
    {
        public bool Active { get; set; }
        public int ToastEnabled { get; set; }      // -1 = valor não existia
        public int GlobalToasts { get; set; }      // -1 = valor não existia
    }

    /// <summary>Silencia os pop-ups de notificação do Windows enquanto o Performance estiver ativo.</summary>
    public static class NotificationSilencer
    {
        private const string PushKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        private const string PushValue = "ToastEnabled";
        private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
        private const string SettingsValue = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

        public static void Silence(AppConfig cfg)
        {
            if (cfg.NotificationBackup != null && cfg.NotificationBackup.Active) return; // já silenciado
            try
            {
                var backup = new NotificationBackup
                {
                    Active = true,
                    ToastEnabled = ReadDword(PushKey, PushValue),
                    GlobalToasts = ReadDword(SettingsKey, SettingsValue)
                };
                cfg.NotificationBackup = backup;
                SaveQuietly(cfg); // grava ANTES de mexer: se o app travar, o próximo início desfaz

                WriteDword(PushKey, PushValue, 0);
                WriteDword(SettingsKey, SettingsValue, 0);
                Log.Info("Notificações do Windows silenciadas durante o Performance");
            }
            catch (Exception ex)
            {
                Log.Error("Silenciar notificações", ex);
            }
        }

        public static void Restore(AppConfig cfg)
        {
            NotificationBackup b = cfg.NotificationBackup;
            if (b == null || !b.Active) return;
            try
            {
                Put(PushKey, PushValue, b.ToastEnabled);
                Put(SettingsKey, SettingsValue, b.GlobalToasts);
                Log.Info("Notificações do Windows restauradas");
            }
            catch (Exception ex)
            {
                Log.Error("Restaurar notificações", ex);
            }
            cfg.NotificationBackup = null;
            SaveQuietly(cfg);
        }

        private static void Put(string key, string name, int original)
        {
            if (original < 0)
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key, true))
                    if (k != null && k.GetValue(name) != null) k.DeleteValue(name, false);
            }
            else
            {
                WriteDword(key, name, original);
            }
        }

        private static int ReadDword(string key, string name)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key, false))
            {
                object v = k != null ? k.GetValue(name) : null;
                return v is int ? (int)v : -1;
            }
        }

        private static void WriteDword(string key, string name, int value)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
                if (k != null) k.SetValue(name, value, RegistryValueKind.DWord);
        }

        private static void SaveQuietly(AppConfig cfg)
        {
            try { cfg.Save(); } catch (Exception ex) { Log.Error("Salvar configuração", ex); }
        }
    }
}
