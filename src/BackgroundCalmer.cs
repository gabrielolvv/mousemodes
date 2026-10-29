using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Um processo acalmado. Fica no config para ser desfeito mesmo depois de um travamento.</summary>
    public class CalmedProcess
    {
        public int Pid { get; set; }
        public string Name { get; set; }
        /// <summary>Início do processo (FILETIME UTC): o Windows reaproveita PIDs, e o outro processo não é nosso.</summary>
        public long StartTime { get; set; }
        public ProcessPriorityClass Priority { get; set; }
        /// <summary>true se o modo de eficiência foi ligado por nós (e precisa ser desligado).</summary>
        public bool Eco { get; set; }
        /// <summary>true se foi posto nos núcleos de eficiência por nós (processador híbrido).</summary>
        public bool Cores { get; set; }
        /// <summary>Quando foi acalmado (FILETIME UTC): o que ele abrir depois nasce com a prioridade herdada.</summary>
        public long CalmedAt { get; set; }
    }

    /// <summary>
    /// Segundo plano calmo: no Performance, os programas desta sessão que estão em segundo plano passam para a
    /// prioridade abaixo do normal, para o modo de eficiência do Windows 11 (EcoQoS) e, nos processadores híbridos,
    /// para os núcleos de eficiência. Nada é fechado.
    /// O jogo, o programa em primeiro plano, os processos do Windows e os da lista NeverCalm ficam como estão.
    ///
    /// O Windows passa a prioridade abaixo do normal para os processos que um programa acalmado abrir (o modo de
    /// eficiência e os núcleos não passam). Esses filhos são adotados: voltam ao normal junto com o pai, e um jogo
    /// aberto pela Steam acalmada volta ao normal na hora.
    /// </summary>
    public class BackgroundCalmer
    {
        private const int ProcessPowerThrottling = 4;
        private const uint ThrottlingVersion = 1;
        private const uint ExecutionSpeed = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

        private const uint SnapProcess = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry32
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int PriorityClassBase;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// Nunca acalma (começo do nome): voz, música, transmissão e gravação, overlays, software do mouse e anti-cheat.
        /// Rebaixar qualquer um destes atrapalharia o jogo em vez de ajudar.
        /// </summary>
        internal static readonly string[] NeverCalm =
        {
            "Discord", "TeamSpeak", "ts3client", "Mumble", "Spotify", "obs", "Streamlabs", "XSplit", "Medal",
            "gameoverlayui", "GameBar", "nvcontainer", "NVIDIA", "RTSS", "MSIAfterburner",
            "lghub", "LogiOverlay", "Razer", "SteelSeries", "iCUE", "vgtray", "FACEIT", "EasyAntiCheat", "BEService"
        };

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly int self;
        private readonly int session;
        private readonly Dictionary<int, CalmedProcess> calmed = new Dictionary<int, CalmedProcess>();
        private readonly HashSet<int> skipped = new HashSet<int>(); // já avaliados: não abre de novo a cada 3 s
        private bool reported;

        /// <summary>Só os testes mudam: limita a estes PIDs, para nunca mexer nos programas de verdade do PC.</summary>
        internal Predicate<int> Scope;

        /// <param name="save">Como gravar o backup (padrão: o config do app). Os testes gravam numa pasta temporária.</param>
        public BackgroundCalmer(AppConfig cfg, Action save = null)
        {
            this.cfg = cfg;
            this.save = save ?? cfg.Save;
            using (Process me = Process.GetCurrentProcess())
            {
                self = me.Id;
                session = me.SessionId;
            }
        }

        public bool Active { get { return calmed.Count > 0; } }

        /// <summary>Regra de quem pode ser acalmado. Só mexe em quem está no Normal: não rebaixa o que já foi ajustado.</summary>
        internal static bool ShouldCalm(string name, ProcessPriorityClass priority, bool sameSession,
                                        IList<string> games, string foreground)
        {
            if (!sameSession || priority != ProcessPriorityClass.Normal || string.IsNullOrEmpty(name)) return false;
            if (string.Equals(name, foreground, StringComparison.OrdinalIgnoreCase)) return false;
            if (AppCloser.Protected.Contains(name)) return false;
            if (GameBooster.Matches(name, games)) return false;
            return !GameBooster.Matches(name, NeverCalm);
        }

        /// <summary>Acalma os processos novos. Chamado ao entrar no Performance e depois a cada checagem de jogos.</summary>
        public void Calm(IList<string> games, string foreground)
        {
            var alive = new HashSet<int>();
            var names = new List<string>();
            bool changed = false;
            Dictionary<int, int> parents = null; // só é lido se aparecer um processo abaixo do normal

            foreach (Process proc in Process.GetProcesses())
            {
                try
                {
                    alive.Add(proc.Id);
                    if (proc.Id == self || calmed.ContainsKey(proc.Id) || skipped.Contains(proc.Id)) continue;
                    if (Scope != null && !Scope(proc.Id)) continue;
                    string name = proc.ProcessName;
                    ProcessPriorityClass priority = proc.PriorityClass;
                    CalmedProcess parent = priority == ProcessPriorityClass.BelowNormal ? CalmedParent(proc, ref parents) : null;
                    // O primeiro plano muda: reavalia quando ele for para trás (por isso não vai para skipped).
                    if (string.Equals(name, foreground, StringComparison.OrdinalIgnoreCase))
                    {
                        if (parent != null) proc.PriorityClass = ProcessPriorityClass.Normal;
                        continue;
                    }
                    // Quem herdou a prioridade baixa de um programa acalmado nasceria no Normal: é julgado como tal.
                    if (parent != null) priority = ProcessPriorityClass.Normal;
                    if (proc.SessionId != session || !ShouldCalm(name, priority, true, games, foreground))
                    {
                        if (parent != null) GiveBackInherited(proc, name, parent, games);
                        skipped.Add(proc.Id);
                        continue;
                    }

                    var entry = new CalmedProcess
                    {
                        Pid = proc.Id,
                        Name = name,
                        StartTime = proc.StartTime.ToFileTimeUtc(),
                        Priority = priority,
                        CalmedAt = DateTime.UtcNow.ToFileTimeUtc()
                    };
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    // Registra antes do modo de eficiência: se ele falhar, a prioridade ainda é desfeita depois.
                    calmed[proc.Id] = entry;
                    changed = true;
                    if (!names.Contains(name)) names.Add(name);
                    entry.Eco = TurnOnEco(proc);
                    entry.Cores = CpuSets.MoveToBackground(proc.Handle);
                }
                catch
                {
                    // Acesso negado (processo elevado ou protegido) ou já encerrado: deixa como está.
                    skipped.Add(proc.Id);
                }
                finally
                {
                    proc.Dispose();
                }
            }

            skipped.RemoveWhere(pid => !alive.Contains(pid));
            var dead = new List<int>();
            foreach (int pid in calmed.Keys) if (!alive.Contains(pid)) dead.Add(pid);
            foreach (int pid in dead) calmed.Remove(pid);

            if (changed || dead.Count > 0) SaveBackup();
            if (!reported && calmed.Count > 0)
            {
                reported = true;
                names.Sort(StringComparer.OrdinalIgnoreCase);
                uint[] cores = CpuSets.Background;
                Log.Info(L.T("Segundo plano calmo: ", "Calm background: ") + calmed.Count +
                         L.T(" processo(s) com prioridade abaixo do normal", " process(es) at below normal priority") +
                         (cores == null ? "" : L.T(" e nos núcleos de eficiência (", " and on the efficiency cores (") +
                                               cores.Length + L.T(" de ", " of ") + CpuSets.Total + ")") +
                         " [" + string.Join(", ", names.ToArray()) + "]");
            }
        }

        /// <summary>
        /// O programa acalmado que abriu este processo, se ele nasceu depois disso (e por isso herdou a prioridade
        /// baixa). null se não for o caso.
        /// </summary>
        private CalmedProcess CalmedParent(Process proc, ref Dictionary<int, int> parents)
        {
            if (calmed.Count == 0) return null;
            if (parents == null) parents = ParentIds();
            int parentId;
            CalmedProcess parent;
            if (!parents.TryGetValue(proc.Id, out parentId) || !calmed.TryGetValue(parentId, out parent)) return null;
            return proc.StartTime.ToFileTimeUtc() >= parent.CalmedAt ? parent : null;
        }

        /// <summary>Herdou a prioridade baixa, mas não é para acalmar (jogo, Windows, voz...): volta ao normal.</summary>
        private static void GiveBackInherited(Process proc, string name, CalmedProcess parent, IList<string> games)
        {
            proc.PriorityClass = ProcessPriorityClass.Normal;
            if (GameBooster.Matches(name, games))
                Log.Info(name + L.T(" abriu pelo ", " was opened by ") + parent.Name +
                         L.T(" acalmado e herdou a prioridade abaixo do normal; devolvida ao normal",
                             " while it was calmed and inherited below normal priority; set back to normal"));
        }

        /// <summary>PID de cada processo → PID de quem o abriu (uma foto de todos os processos, sem abrir nenhum).</summary>
        internal static Dictionary<int, int> ParentIds()
        {
            var parents = new Dictionary<int, int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return parents;
            try
            {
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry32)) };
                for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                    parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            finally
            {
                CloseHandle(snapshot);
            }
            return parents;
        }

        /// <summary>O programa foi para o primeiro plano (alt-tab): devolve o normal para ele enquanto estiver lá.</summary>
        public void Release(string foreground)
        {
            if (string.IsNullOrEmpty(foreground)) return;
            var released = new List<int>();
            foreach (CalmedProcess entry in calmed.Values)
                if (string.Equals(entry.Name, foreground, StringComparison.OrdinalIgnoreCase)) released.Add(entry.Pid);
            if (released.Count == 0) return;
            foreach (int pid in released)
            {
                Restore(calmed[pid]);
                calmed.Remove(pid);
            }
            SaveBackup();
        }

        public void RestoreAll()
        {
            int count = 0;
            foreach (CalmedProcess entry in calmed.Values)
                if (Restore(entry)) count++;
            if (calmed.Count > 0)
                Log.Info(L.T("Segundo plano calmo desfeito em ", "Calm background undone on ") + count + L.T(" processo(s)", " process(es)"));
            calmed.Clear();
            skipped.Clear();
            reported = false;
            if (cfg.CalmBackup != null && cfg.CalmBackup.Count > 0) SaveBackup();
        }

        /// <summary>O app caiu com o segundo plano acalmado: desfaz nos processos que ainda são os mesmos.</summary>
        public static void Recover(AppConfig cfg, Action save = null)
        {
            if (cfg.CalmBackup == null || cfg.CalmBackup.Count == 0) return;
            int count = 0;
            foreach (CalmedProcess entry in cfg.CalmBackup)
                if (Restore(entry)) count++;
            Log.Warn(L.T("O app foi fechado com o segundo plano acalmado; desfeito em ", "The app closed with the background calmed; undone on ") +
                     count + L.T(" processo(s) ainda abertos", " process(es) still open"));
            cfg.CalmBackup = new List<CalmedProcess>();
            Save(save ?? cfg.Save);
        }

        private void SaveBackup()
        {
            cfg.CalmBackup = new List<CalmedProcess>(calmed.Values);
            Save(save);
        }

        private static void Save(Action save)
        {
            try { save(); } catch (Exception ex) { Log.Error(L.T("Salvar configuração", "Save settings"), ex); }
        }

        /// <summary>Devolve prioridade, eficiência e núcleos, se o processo ainda for o mesmo e ninguém tiver mudado depois.</summary>
        private static bool Restore(CalmedProcess entry)
        {
            try
            {
                using (Process p = Process.GetProcessById(entry.Pid))
                {
                    if (p.HasExited || p.StartTime.ToFileTimeUtc() != entry.StartTime) return false;
                    if (p.PriorityClass == ProcessPriorityClass.BelowNormal) p.PriorityClass = entry.Priority;
                    if (entry.Eco) SetEco(p.Handle, false);
                    if (entry.Cores) CpuSets.Clear(p.Handle);
                    return true;
                }
            }
            catch
            {
                return false; // já fechou
            }
        }

        /// <summary>
        /// Liga o modo de eficiência, a não ser que o próprio programa já controle isso (navegadores fazem).
        /// false se não ligou: Windows antigo, acesso negado ou programa que já cuida disso.
        /// </summary>
        private static bool TurnOnEco(Process proc)
        {
            try
            {
                IntPtr handle = proc.Handle;
                var current = new PowerThrottlingState { Version = ThrottlingVersion };
                if (GetProcessInformation(handle, ProcessPowerThrottling, ref current, Marshal.SizeOf(current)) &&
                    (current.ControlMask & ExecutionSpeed) != 0)
                    return false;
                return SetEco(handle, true);
            }
            catch
            {
                return false; // acesso negado ou Windows sem SetProcessInformation
            }
        }

        private static bool SetEco(IntPtr handle, bool on)
        {
            try
            {
                // Desligar = devolver a decisão ao Windows (ControlMask 0), que é o padrão de todo processo.
                var state = new PowerThrottlingState
                {
                    Version = ThrottlingVersion,
                    ControlMask = on ? ExecutionSpeed : 0,
                    StateMask = on ? ExecutionSpeed : 0
                };
                return SetProcessInformation(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf(state));
            }
            catch
            {
                return false;
            }
        }
    }
}
