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
            if (hook == IntPtr.Zero) Log.Warn("Aviso de troca de janela indisponível");
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

            Process[] processes = Process.GetProcesses();
            try
            {
                foreach (Process proc in processes)
                {
                    string procName;
                    try { procName = proc.ProcessName; }
                    catch { continue; }

                    foreach (string prefix in prefixes)
                    {
                        if (procName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            return procName;
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
