using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MouseModes
{
    public static class GameDetector
    {
        /// <summary>
        /// Retorna o item da lista (ex.: "FortniteClient-Win64-Shipping") cujo processo está aberto, ou null.
        /// Devolve o item, não o nome do processo: assim o jogo e os processos auxiliares dele
        /// (ex.: "..._EAC_EOS" do anti-cheat) contam como o mesmo jogo.
        /// </summary>
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
                            return prefix;
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
