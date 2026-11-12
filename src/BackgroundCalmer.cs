using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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

    /// <summary>Um processo acalmado ou adotado neste Performance, guardado mesmo depois de ele fechar.</summary>
    internal sealed class CalmedRecord
    {
        public int Pid;
        /// <summary>Desde quando ele passa a prioridade baixa adiante (FILETIME UTC).</summary>
        public long CalmedAt;
        /// <summary>Quando ele fechou (FILETIME UTC); 0 = visto aberto até agora.</summary>
        public long Gone;
        /// <summary>
        /// Aberto enquanto ele não fecha: com um handle aberto o Windows não reaproveita o PID, e o handle diz quando ele
        /// fechou. Fechado assim que o Gone é marcado (ou no RestoreAll). null se o Windows não deu o handle: aí o Gone
        /// vem da checagem que já não o viu aberto.
        /// </summary>
        public SafeProcessHandle Handle;
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

        private const uint Synchronize = 0x00100000;
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const uint WaitObject0 = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessTimes(SafeProcessHandle handle, out long creation, out long exit, out long kernel, out long user);

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
        /// <summary>Quando o segundo plano começou a ser acalmado (FILETIME UTC); 0 = não está acalmado.</summary>
        private long since;
        /// <summary>Quantos níveis acima procurar o programa acalmado (Steam → bootstrapper do anti-cheat → jogo).</summary>
        private const int MaxAncestors = 4;

        /// <summary>
        /// Todo processo que este Performance acalmou ou adotou, mesmo os que já fecharam (o de um cmd /c start fecha
        /// logo). É a prova para adotar um órfão: quem o abriu é um destes. Limpo ao sair do Performance.
        /// </summary>
        private readonly List<CalmedRecord> history = new List<CalmedRecord>();

        /// <summary>Só os testes mudam: limita a estes PIDs, para nunca mexer nos programas de verdade do PC.</summary>
        internal Predicate<int> Scope;

        /// <summary>
        /// Se o processo é um jogo da lista (e não um componente do Windows que bate com um item curto). Só os testes
        /// mudam: o ping que faz o papel do jogo fica na pasta do Windows.
        /// </summary>
        internal Func<Process, string, IList<string>, bool> IsGame = GameDetector.IsListedGame;

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
            long scanned = DateTime.UtcNow.ToFileTimeUtc(); // antes da foto: quem não estiver nela fechou antes disto
            if (since == 0) since = scanned;
            MarkExited(history); // antes de julgar órfãos: quem fechou, com a hora exata

            // Entrou na lista de jogos depois de acalmado (Ajustes salvos no meio do Performance): volta ao normal.
            var nowGames = new List<int>();
            foreach (CalmedProcess entry in calmed.Values)
                if (GameBooster.Matches(entry.Name, games)) nowGames.Add(entry.Pid);
            foreach (int pid in nowGames)
            {
                Restore(calmed[pid]);
                calmed.Remove(pid);
                changed = true;
            }

            foreach (Process proc in Process.GetProcesses())
            {
                try
                {
                    alive.Add(proc.Id);
                    if (proc.Id == self || calmed.ContainsKey(proc.Id) || skipped.Contains(proc.Id)) continue;
                    if (Scope != null && !Scope(proc.Id)) continue;
                    // Outra sessão (serviços, outro usuário): nunca é nosso, nem como herdeiro. Antes de qualquer outra coisa.
                    if (proc.SessionId != session)
                    {
                        skipped.Add(proc.Id);
                        continue;
                    }
                    string name = proc.ProcessName;
                    ProcessPriorityClass priority = proc.PriorityClass;
                    long started = proc.StartTime.ToFileTimeUtc();
                    CalmedProcess parent = priority == ProcessPriorityClass.BelowNormal ? CalmedAncestor(proc.Id, started, ref parents) : null;
                    bool lateBelowNormal = parent == null && priority == ProcessPriorityClass.BelowNormal && started >= since;
                    // Um jogo abaixo do normal que abriu depois de o segundo plano ser acalmado herdou isso de alguém,
                    // mesmo que o processo do meio (launcher, bootstrapper do anti-cheat) já tenha fechado. Componentes de
                    // jogo do Windows que batem com um item curto da lista ("Game") não contam.
                    // Fora os jogos, só com prova: quem o abriu já fechou, mas foi um programa que este Performance acalmou
                    // ou adotou (cmd /c start, Update.exe --processStart do Squirrel, stub de launcher acalmados). Uma
                    // tarefa agendada também nasce abaixo do normal, e essa não é nossa.
                    bool inherited = parent != null ||
                                     (lateBelowNormal && IsGame(proc, name, games)) ||
                                     (lateBelowNormal && !AppCloser.Protected.Contains(name) &&
                                      OrphanOfCalmed(proc.Id, started, ref parents));
                    // O primeiro plano muda: reavalia quando ele for para trás (por isso não vai para skipped).
                    if (string.Equals(name, foreground, StringComparison.OrdinalIgnoreCase))
                    {
                        if (inherited) proc.PriorityClass = ProcessPriorityClass.Normal;
                        continue;
                    }
                    // Quem herdou a prioridade baixa de um programa acalmado nasceria no Normal: é julgado como tal.
                    if (inherited) priority = ProcessPriorityClass.Normal;
                    if (!ShouldCalm(name, priority, true, games, foreground))
                    {
                        if (inherited) GiveBackInherited(proc, name, parent, games);
                        skipped.Add(proc.Id);
                        continue;
                    }

                    var entry = new CalmedProcess
                    {
                        Pid = proc.Id,
                        Name = name,
                        StartTime = started,
                        Priority = priority,
                        // Adotado: ele passa a prioridade baixa adiante desde que nasceu, não desde agora.
                        CalmedAt = inherited ? started : DateTime.UtcNow.ToFileTimeUtc()
                    };
                    proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                    // Registra antes do modo de eficiência: se ele falhar, a prioridade ainda é desfeita depois.
                    calmed[proc.Id] = entry;
                    Remember(entry);
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
            // Sem handle, a única pista é o PID sumir da foto.
            foreach (CalmedRecord record in history)
                if (record.Gone == 0 && record.Handle == null && !alive.Contains(record.Pid)) record.Gone = scanned;
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
        /// Guarda no histórico o processo que acabou de ser acalmado ou adotado, com um handle que segura o PID. Se ele já
        /// está lá, aberto e com handle (voltou do primeiro plano e foi acalmado de novo), não abre outro.
        /// </summary>
        private void Remember(CalmedProcess entry)
        {
            foreach (CalmedRecord known in history)
                if (known.Pid == entry.Pid && known.Gone == 0 && known.Handle != null) return;
            var record = new CalmedRecord { Pid = entry.Pid, CalmedAt = entry.CalmedAt };
            history.Add(record); // antes de abrir: daqui em diante o RestoreAll fecha o handle
            record.Handle = OpenTracked(entry.Pid, entry.StartTime);
        }

        /// <summary>
        /// Handle só para esperar e ler as horas do processo, se ele ainda for o que começou em <paramref name="started"/>
        /// (FILETIME UTC). null se não deu para abrir ou se o PID já é de outro processo.
        /// </summary>
        internal static SafeProcessHandle OpenTracked(int pid, long started)
        {
            SafeProcessHandle handle = null;
            try
            {
                handle = OpenProcess(Synchronize | ProcessQueryLimitedInformation, false, pid);
                long creation, exit, kernel, user;
                if (handle.IsInvalid || !GetProcessTimes(handle, out creation, out exit, out kernel, out user) || creation != started)
                    return null;
                SafeProcessHandle same = handle;
                handle = null;
                return same;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (handle != null) handle.Dispose();
            }
        }

        /// <summary>
        /// Marca o Gone de quem fechou, pela hora de saída que o Windows guarda no processo (ou agora, se não der para ler:
        /// com o handle aberto o PID não foi reaproveitado até aqui), e fecha o handle.
        /// </summary>
        internal static void MarkExited(IList<CalmedRecord> history)
        {
            foreach (CalmedRecord record in history)
            {
                if (record.Handle == null || record.Gone != 0) continue;
                long exitTime = 0;
                try
                {
                    if (WaitForSingleObject(record.Handle, 0) != WaitObject0) continue; // ainda aberto
                    long creation, kernel, user;
                    if (!GetProcessTimes(record.Handle, out creation, out exitTime, out kernel, out user)) exitTime = 0;
                }
                catch
                {
                    exitTime = 0;
                }
                record.Gone = exitTime > 0 ? exitTime : DateTime.UtcNow.ToFileTimeUtc();
                DropHandle(record);
            }
        }

        private static void DropHandle(CalmedRecord record)
        {
            if (record.Handle == null) return;
            record.Handle.Dispose();
            record.Handle = null;
        }

        /// <summary>
        /// O programa acalmado de quem este processo descende (pai, avô...), se ele nasceu depois de o antepassado ser
        /// acalmado (e por isso herdou a prioridade baixa). null se não for o caso.
        /// </summary>
        private CalmedProcess CalmedAncestor(int pid, long started, ref Dictionary<int, int> parents)
        {
            if (calmed.Count == 0) return null;
            if (parents == null) parents = ParentIds();
            for (int level = 0; level < MaxAncestors; level++)
            {
                int parentId;
                if (!parents.TryGetValue(pid, out parentId) || parentId == pid || parentId == 0) return null;
                CalmedProcess ancestor;
                if (calmed.TryGetValue(parentId, out ancestor)) return started >= ancestor.CalmedAt ? ancestor : null;
                pid = parentId;
            }
            return null;
        }

        /// <summary>
        /// Órfão de um programa acalmado: quem abriu este processo já fechou e era um que este Performance acalmou ou
        /// adotou (e que já estava abaixo do normal quando ele nasceu). Sem essa prova, não é nosso.
        /// </summary>
        private bool OrphanOfCalmed(int pid, long started, ref Dictionary<int, int> parents)
        {
            if (history.Count == 0) return false;
            if (parents == null) parents = ParentIds();
            int parentId;
            if (!parents.TryGetValue(pid, out parentId) || parentId == pid || parentId == 0) return false;
            return ParentGone(parentId, started, parents) && LaunchedByCalmed(history, parentId, started);
        }

        /// <summary>
        /// Quem abriu o processo já fechou (não está na foto, ou o PID agora é de um processo mais novo que ele).
        /// false se não der para saber.
        /// </summary>
        private static bool ParentGone(int parentId, long started, Dictionary<int, int> parents)
        {
            if (!parents.ContainsKey(parentId)) return true;
            try
            {
                // O Windows reaproveita PIDs: um "pai" que começou depois do filho é outro processo.
                using (Process p = Process.GetProcessById(parentId))
                    return p.StartTime.ToFileTimeUtc() > started;
            }
            catch (ArgumentException)
            {
                return true; // fechou depois da foto
            }
            catch
            {
                return false; // acesso negado (processo elevado ou do sistema): está vivo
            }
        }

        /// <summary>
        /// O PID que abriu um processo nascido em <paramref name="started"/> é de um processo acalmado ou adotado: com
        /// esse PID, já abaixo do normal antes de ele nascer (CalmedAt), e não visto fechado antes disso (Gone).
        /// </summary>
        internal static bool LaunchedByCalmed(IList<CalmedRecord> history, int parentId, long started)
        {
            foreach (CalmedRecord record in history)
                if (record.Pid == parentId && record.CalmedAt <= started && (record.Gone == 0 || started < record.Gone))
                    return true;
            return false;
        }

        /// <summary>Herdou a prioridade baixa, mas não é para acalmar (jogo, Windows, voz...): volta ao normal.</summary>
        private static void GiveBackInherited(Process proc, string name, CalmedProcess parent, IList<string> games)
        {
            proc.PriorityClass = ProcessPriorityClass.Normal;
            if (GameBooster.Matches(name, games))
                Log.Info(name + L.T(" abriu por um programa acalmado", " was opened by a calmed program") +
                         (parent != null ? " (" + parent.Name + ")" : "") +
                         L.T(" e herdou a prioridade abaixo do normal; devolvida ao normal",
                             " and inherited below normal priority; set back to normal"));
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
            foreach (CalmedRecord record in history) DropHandle(record);
            history.Clear();
            reported = false;
            since = 0;
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
