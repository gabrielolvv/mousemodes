using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MouseModes
{
    /// <summary>Sobe a prioridade dos processos do jogo para Alta e devolve a original depois.</summary>
    public class GameBooster
    {
        private readonly Dictionary<int, ProcessPriorityClass> boosted = new Dictionary<int, ProcessPriorityClass>();
        private readonly HashSet<int> denied = new HashSet<int>(); // não tenta de novo a cada 3 s

        public bool AnyBoosted { get { return boosted.Count > 0; } }

        /// <summary>Retorna quantos processos novos foram ajustados. Processos protegidos por anti-cheat são ignorados.</summary>
        public int Boost(IList<string> games)
        {
            int count = 0;
            foreach (Process proc in Process.GetProcesses())
            {
                string name = null;
                try
                {
                    if (boosted.ContainsKey(proc.Id) || denied.Contains(proc.Id)) continue;
                    name = proc.ProcessName;
                    if (!Matches(name, games)) continue;
                    ProcessPriorityClass original = proc.PriorityClass;
                    // Só mexe em quem está no normal ou abaixo; não rebaixa nada.
                    if (original == ProcessPriorityClass.Normal ||
                        original == ProcessPriorityClass.BelowNormal ||
                        original == ProcessPriorityClass.Idle)
                    {
                        proc.PriorityClass = ProcessPriorityClass.High;
                        boosted[proc.Id] = original;
                        count++;
                        Log.Info(L.T("Prioridade Alta: ", "High priority: ") + name + " (PID " + proc.Id + L.T(", antes ", ", was ") + original + ")");
                    }
                }
                catch (Exception ex)
                {
                    // Acesso negado (anti-cheat, processo protegido) ou processo já encerrado.
                    if (name != null)
                    {
                        denied.Add(proc.Id);
                        Log.Warn(L.T("Prioridade não alterada em ", "Priority not changed on ") + name + ": " + ex.Message + L.T(" (anti-cheat ou processo protegido)", " (anti-cheat or protected process)"));
                    }
                }
                finally
                {
                    proc.Dispose();
                }
            }
            return count;
        }

        /// <summary>Esquece processos que já fecharam.</summary>
        public void Prune()
        {
            var dead = new List<int>();
            foreach (int pid in boosted.Keys)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                        if (p.HasExited) dead.Add(pid);
                }
                catch { dead.Add(pid); }
            }
            foreach (int pid in dead) boosted.Remove(pid);
        }

        public void RestoreAll()
        {
            foreach (KeyValuePair<int, ProcessPriorityClass> kv in boosted)
            {
                try
                {
                    using (Process p = Process.GetProcessById(kv.Key))
                        if (!p.HasExited) p.PriorityClass = kv.Value;
                }
                catch { }
            }
            if (boosted.Count > 0) Log.Info(L.T("Prioridade original devolvida a ", "Original priority restored on ") + boosted.Count + L.T(" processo(s)", " process(es)"));
            boosted.Clear();
            denied.Clear();
        }

        internal static bool Matches(string processName, IList<string> prefixes)
        {
            foreach (string entry in prefixes)
            {
                string prefix = GameDetector.Normalize(entry);
                if (prefix.Length > 0 && processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Fecha programas da lista ao entrar no modo Performance (em segundo plano, sem travar a tela).</summary>
    public static class AppCloser
    {
        // Nunca fecha estes, mesmo que estejam na lista por engano.
        private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "dwm", "csrss", "winlogon", "wininit", "services", "lsass", "smss", "svchost",
            "System", "Idle", "Registry", "fontdrvhost", "sihost", "ctfmon", "audiodg", "conhost",
            "RuntimeBroker", "StartMenuExperienceHost", "SearchHost", "ShellExperienceHost",
            "TextInputHost", "taskhostw", "spoolsv", "MsMpEng", "SecurityHealthService", "MouseModes"
        };

        /// <summary>Resultado: (fechados, que não fecharam).</summary>
        public static void CloseAsync(IList<string> apps, IList<string> games, bool force, Action<List<string>, List<string>> done)
        {
            var appList = new List<string>(apps);
            var gameList = new List<string>(games);
            var thread = new Thread(() =>
            {
                var closed = new List<string>();
                var failed = new List<string>();
                try
                {
                    Run(appList, gameList, force, closed, failed);
                }
                catch { }
                if (done != null) done(closed, failed);
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private static void Run(List<string> apps, List<string> games, bool force, List<string> closed, List<string> failed)
        {
            int self = Process.GetCurrentProcess().Id;
            var targets = new List<Process>();
            foreach (Process proc in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    string name = proc.ProcessName;
                    keep = proc.Id != self
                           && !Protected.Contains(name)
                           && !GameBooster.Matches(name, games)
                           && GameBooster.Matches(name, apps);
                }
                catch { }
                if (keep) targets.Add(proc); else proc.Dispose();
            }

            // 1) Pede para fechar normalmente (como clicar no X).
            foreach (Process p in targets)
            {
                try { p.CloseMainWindow(); } catch { }
            }

            // 2) Espera até 4 segundos.
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            foreach (Process p in targets)
            {
                try
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    p.WaitForExit(left);
                }
                catch { }
            }

            // 3) Quem não fechou: força, se permitido.
            foreach (Process p in targets)
            {
                string name = "?";
                try
                {
                    name = p.ProcessName;
                    if (p.HasExited)
                    {
                        AddOnce(closed, name);
                    }
                    else if (force)
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                        AddOnce(closed, name);
                    }
                    else
                    {
                        AddOnce(failed, name);
                    }
                }
                catch
                {
                    // Já fechou entre uma etapa e outra, ou acesso negado.
                    try { if (p.HasExited) AddOnce(closed, name); else AddOnce(failed, name); }
                    catch { AddOnce(closed, name); }
                }
                finally
                {
                    p.Dispose();
                }
            }
            failed.RemoveAll(x => closed.Contains(x));
            if (targets.Count > 0)
                Log.Info(L.T("Fechar programas: fechados [", "Close programs: closed [") + string.Join(", ", closed.ToArray()) + L.T("], não fecharam [", "], did not close [") +
                         string.Join(", ", failed.ToArray()) + "]" + (force ? L.T(" (forçar ligado)", " (force on)") : ""));
        }

        private static void AddOnce(List<string> list, string name)
        {
            if (!list.Exists(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) list.Add(name);
        }
    }
}
