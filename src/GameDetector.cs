using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>
    /// Avisa quando a janela em primeiro plano muda de processo. Os avisos chegam pelo laço de mensagens
    /// da thread que criou o objeto (a da tela), sem consulta periódica.
    /// </summary>
    public sealed class ForegroundWatcher : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private readonly WinEventProc proc; // referência guardada para o GC não recolher o delegate
        private IntPtr hook;
        private uint pid;

        /// <summary>Nome do processo em primeiro plano (sem .exe), ou null.</summary>
        public string ProcessName { get; private set; }

        /// <summary>O processo em primeiro plano mudou (nome novo, pode ser null).</summary>
        public event Action<string> Changed;

        public ForegroundWatcher()
        {
            proc = OnForeground;
            Update(GetForegroundWindow());
            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (hook == IntPtr.Zero) Log.Warn(L.T("Aviso de troca de janela indisponível", "Window change notice unavailable"));
        }

        private void OnForeground(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (Update(hwnd))
            {
                var handler = Changed;
                if (handler != null) handler(ProcessName);
            }
        }

        /// <summary>Retorna true se o processo em primeiro plano mudou.</summary>
        private bool Update(IntPtr hwnd)
        {
            uint newPid;
            GetWindowThreadProcessId(hwnd, out newPid);
            if (newPid == pid) return false;
            pid = newPid;

            string name = null;
            if (newPid != 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById((int)newPid))
                        name = p.ProcessName;
                }
                catch { } // fechou no meio do caminho
            }
            if (name == ProcessName) return false;
            ProcessName = name;
            return true;
        }

        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWinEvent(hook);
            hook = IntPtr.Zero;
        }
    }

    public static class GameDetector
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// Componentes de jogo do próprio Windows que não ficam na pasta do Windows (a Xbox Game Bar vem da Store).
        /// Começo do nome, como a lista de jogos.
        /// </summary>
        internal static readonly string[] WindowsGaming = { "GameBar", "GameInput", "GamingServices", "XboxGameBar", "XboxPcApp" };

        private static int mySession = -1;

        /// <summary>
        /// Se um processo cujo nome bateu com a lista pode ser o jogo. Um item curto como "Game" (o Game.exe do RPG Maker
        /// e do Godot) também pega GameBarPresenceWriter, GameInputSvc...: serviços (outra sessão), o que roda da pasta do
        /// Windows e os componentes de jogo do Windows nunca contam. Caminho vazio (sem acesso) não exclui.
        /// </summary>
        internal static bool CanBeGame(string name, int sessionId, int userSession, string imagePath, string windowsDir)
        {
            if (sessionId != userSession) return false;
            if (GameBooster.Matches(name, WindowsGaming)) return false;
            if (string.IsNullOrEmpty(imagePath) || string.IsNullOrEmpty(windowsDir)) return true;
            string dir = windowsDir.TrimEnd('\\') + "\\";
            return !imagePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Caminho do .exe do processo, sem abrir mais que o mínimo; null se não der (acesso negado, já fechou).</summary>
        private static string ImagePath(int pid)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var buffer = new System.Text.StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>Retorna o nome do primeiro processo aberto que começa com algum item da lista, ou null.</summary>
        public static string FindRunning(IList<string> games)
        {
            var prefixes = new List<string>();
            foreach (string g in games)
            {
                string name = Normalize(g);
                if (name.Length > 0) prefixes.Add(name);
            }
            if (prefixes.Count == 0) return null;

            if (mySession < 0)
                using (Process me = Process.GetCurrentProcess()) mySession = me.SessionId;
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            Process[] processes = Process.GetProcesses();
            try
            {
                foreach (Process proc in processes)
                {
                    string procName;
                    int sessionId;
                    try
                    {
                        procName = proc.ProcessName;
                        sessionId = proc.SessionId;
                    }
                    catch { continue; }

                    foreach (string prefix in prefixes)
                    {
                        if (!procName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        // Só quem bateu com a lista paga a leitura do caminho (a checagem roda a cada 3 s).
                        if (CanBeGame(procName, sessionId, mySession, ImagePath(proc.Id), windowsDir))
                            return procName;
                        break;
                    }
                }
            }
            finally
            {
                foreach (Process proc in processes) proc.Dispose();
            }
            return null;
        }

        public static string Normalize(string entry)
        {
            if (entry == null) return string.Empty;
            string name = entry.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        /// <summary>Processos com janela aberta, para facilitar achar o nome do jogo.</summary>
        public static List<string> ListWindowedProcesses()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero) names.Add(proc.ProcessName);
                }
                catch { }
                finally { proc.Dispose(); }
            }
            return new List<string>(names);
        }
    }
}
