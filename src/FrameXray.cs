using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace MouseModes
{
    /// <summary>Um quadro do jogo: quando ficou pronto (s desde o começo da partida) e quanto levou desde o anterior (ms).</summary>
    public struct FrameSample
    {
        public double At;
        public float Ms;

        public FrameSample(double at, float ms)
        {
            At = at;
            Ms = ms;
        }
    }

    /// <summary>Um programa que usou muito processador num intervalo de leitura (3 s) da partida.</summary>
    public struct CpuBurst
    {
        public double At;
        public string Name;
        public double Percent;

        public CpuBurst(double at, string name, double percent)
        {
            At = at;
            Name = name;
            Percent = percent;
        }
    }

    /// <summary>Uma travadinha: um ou mais quadros muito mais lentos que o normal, em menos de 1 s.</summary>
    public class Stutter
    {
        public double At { get; set; }
        /// <summary>O quadro mais lento dela, em ms.</summary>
        public double Ms { get; set; }
        /// <summary>O que se destacou em segundo plano na hora (null se nada se destacou).</summary>
        public string Cause { get; set; }
    }

    /// <summary>Os quadros do jogo durante a partida.</summary>
    public class FrameReport
    {
        public int Frames { get; set; }
        public double AverageFps { get; set; }
        /// <summary>Média de FPS do 1% de quadros mais lentos: o que se sente nas travadas.</summary>
        public double Low1Fps { get; set; }
        public double Low01Fps { get; set; }
        public double MedianMs { get; set; }
        public int StutterCount { get; set; }
        /// <summary>As piores travadinhas, em ordem de tempo.</summary>
        public List<Stutter> Worst { get; set; }

        /// <summary>"144 FPS de média · 1% low 92 FPS · 7 travadinhas"</summary>
        public string Summary()
        {
            return Fps(AverageFps) + L.T(" FPS de média · 1% low ", " FPS average · 1% low ") + Fps(Low1Fps) + " FPS · " +
                   (StutterCount == 0 ? L.T("nenhuma travadinha", "no stutters")
                                      : StutterCount + (StutterCount == 1 ? L.T(" travadinha", " stutter") : L.T(" travadinhas", " stutters")));
        }

        /// <summary>"aos 23 min · quadro de 85 ms · nessa hora o chrome usava 34% do processador"</summary>
        public static string Describe(Stutter s)
        {
            return L.T("aos ", "at ") + (int)(s.At / 60) + " min · " + L.T("quadro de ", "frame of ") + Math.Round(s.Ms) + " ms · " +
                   (s.Cause ?? L.T("nada em segundo plano se destacou: pode ser do próprio jogo (shaders, carregamento)",
                                   "nothing stood out in the background: it may be the game itself (shaders, loading)"));
        }

        private static string Fps(double fps)
        {
            return Math.Round(fps).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>As contas dos quadros, sem ler o Windows: média, 1% low e as travadinhas com o que havia na hora.</summary>
    public static class FrameStats
    {
        /// <summary>Menos que isso (uns segundos de jogo) não dá para dizer nada.</summary>
        internal const int MinFrames = 300;
        /// <summary>Quadros mais longos que isso são pausa (alt-tab, tela de carregamento), não travadinha.</summary>
        internal const double PauseMs = 1000;
        /// <summary>Os primeiros segundos são o jogo carregando.</summary>
        internal const double WarmupSeconds = 10;
        internal const int MaxWorst = 3;
        private const double SameStutterGap = 1.0;
        private const double CauseWindow = 3.0;
        /// <summary>Processador de um programa, no intervalo, que conta como "estava pesando" na hora de uma travadinha.</summary>
        internal const double CpuBurstPercent = 20.0;

        /// <summary>Travadinha: quadro com pelo menos 2,5 × o normal e 20 ms a mais.</summary>
        internal static double Threshold(double medianMs)
        {
            return Math.Max(medianMs * 2.5, medianMs + 20);
        }

        public static FrameReport Analyze(IList<FrameSample> frames, IList<IoBurst> io, IList<CpuBurst> cpu, IList<NetSample> net)
        {
            if (frames == null) return null;
            var used = new List<FrameSample>();
            foreach (FrameSample f in frames)
                if (f.At >= WarmupSeconds && f.Ms > 0 && f.Ms < PauseMs) used.Add(f);
            if (used.Count < MinFrames) return null;

            var sorted = new List<float>(used.Count);
            double total = 0;
            foreach (FrameSample f in used)
            {
                sorted.Add(f.Ms);
                total += f.Ms;
            }
            sorted.Sort();
            double median = sorted[sorted.Count / 2];

            double threshold = Threshold(median);
            var stutters = new List<Stutter>();
            Stutter open = null;
            foreach (FrameSample f in used)
            {
                if (f.Ms < threshold) continue;
                if (open == null || f.At - open.At > SameStutterGap)
                {
                    open = new Stutter { At = f.At, Ms = f.Ms };
                    stutters.Add(open);
                }
                else if (f.Ms > open.Ms)
                {
                    open.Ms = f.Ms;
                }
            }

            var worst = new List<Stutter>(stutters);
            worst.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            if (worst.Count > MaxWorst) worst.RemoveRange(MaxWorst, worst.Count - MaxWorst);
            foreach (Stutter s in worst) s.Cause = CauseOf(s, io, cpu, net);
            worst.Sort((a, b) => a.At.CompareTo(b.At));

            return new FrameReport
            {
                Frames = used.Count,
                AverageFps = used.Count / (total / 1000.0),
                Low1Fps = LowFps(sorted, 0.01),
                Low01Fps = LowFps(sorted, 0.001),
                MedianMs = median,
                StutterCount = stutters.Count,
                Worst = worst
            };
        }

        /// <summary>FPS médio dos quadros mais lentos (a fração pedida do total; pelo menos 1 quadro).</summary>
        internal static double LowFps(List<float> sortedMs, double fraction)
        {
            int count = Math.Max(1, (int)Math.Round(sortedMs.Count * fraction));
            double sum = 0;
            for (int i = sortedMs.Count - count; i < sortedMs.Count; i++) sum += sortedMs[i];
            return 1000.0 / (sum / count);
        }

        /// <summary>O que mais se destacou perto da travadinha: processador, depois disco, depois a rede.</summary>
        internal static string CauseOf(Stutter s, IList<IoBurst> io, IList<CpuBurst> cpu, IList<NetSample> net)
        {
            CpuBurst? topCpu = null;
            if (cpu != null)
                foreach (CpuBurst b in cpu)
                    if (Math.Abs(b.At - s.At) <= CauseWindow && (topCpu == null || b.Percent > topCpu.Value.Percent)) topCpu = b;
            if (topCpu != null)
                return L.T("nessa hora o ", "meanwhile ") + topCpu.Value.Name + L.T(" usava ", " used ") +
                       Math.Round(topCpu.Value.Percent) + L.T("% do processador", "% of the CPU");

            IoBurst? topIo = null;
            if (io != null)
                foreach (IoBurst b in io)
                    if (Math.Abs(b.At - s.At) <= CauseWindow && (topIo == null || b.Bytes > topIo.Value.Bytes)) topIo = b;
            if (topIo != null)
                return L.T("nessa hora o ", "meanwhile ") + topIo.Value.Name + L.T(" lia/gravava ", " read/wrote ") + MatchReport.FormatBytes(topIo.Value.Bytes);

            if (net != null)
                foreach (NetSample n in net)
                    if (Math.Abs(n.At - s.At) <= 1.5 && n.Internet == NetSample.Lost)
                        return L.T("a internet não respondeu nessa hora (jogo online pode travar esperando o servidor)",
                                   "the internet did not answer at that time (an online game can hitch waiting for the server)");
            return null;
        }
    }

    /// <summary>
    /// Permissão para ler os quadros do jogo: o Windows só deixa ligar o rastreamento (ETW) para administradores ou para
    /// quem está no grupo "Performance Log Users" (S-1-5-32-559). O app põe a conta no grupo uma vez, com o pedido de
    /// administrador do próprio Windows; vale depois de sair e entrar de novo na conta.
    /// </summary>
    public static class FramePermission
    {
        internal const string PerformanceLogUsers = "S-1-5-32-559";
        private const int ErrorCancelled = 1223;

        /// <summary>Esta sessão já pode medir: o grupo está no token (ou o app roda como administrador).</summary>
        public static bool Allowed
        {
            get
            {
                try
                {
                    using (WindowsIdentity me = WindowsIdentity.GetCurrent())
                    {
                        if (new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator)) return true;
                        foreach (IdentityReference group in me.Groups)
                            if (group.Value == PerformanceLogUsers) return true;
                    }
                }
                catch { }
                return false;
            }
        }

        /// <summary>Põe (ou tira) a conta do grupo. Abre o pedido de administrador do Windows. null se deu certo.</summary>
        public static string Change(bool allow)
        {
            try
            {
                string user;
                using (WindowsIdentity me = WindowsIdentity.GetCurrent()) user = me.User.Value;
                string command = (allow ? "Add" : "Remove") + "-LocalGroupMember -SID " + PerformanceLogUsers + " -Member " + user +
                                 " -ErrorAction Stop";
                var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + command + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(info))
                {
                    p.WaitForExit(30000);
                    if (!p.HasExited) return L.T("o Windows não respondeu a tempo", "Windows did not answer in time");
                    // Já estava (ou já não estava) no grupo também conta como certo.
                    return p.ExitCode == 0 ? null : L.T("o Windows recusou a mudança (código ", "Windows refused the change (code ") + p.ExitCode + ")";
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return L.T("cancelado no pedido de administrador", "cancelled at the administrator prompt");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>
    /// Lê, durante a partida, o momento em que cada processo entrega um quadro para a tela: os eventos de Present do
    /// DirectX 9 ao 12 (os mesmos que o PresentMon usa), por uma sessão de rastreamento do Windows (ETW) em tempo real.
    /// Não entra no jogo nem mexe nele. Jogos em Vulkan ou OpenGL não aparecem.
    /// </summary>
    public sealed class FrameProbe
    {
        private const string SessionName = "MouseModes Frame X-ray";
        private static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");   // Microsoft-Windows-DXGI
        private static readonly Guid D3D9Provider = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");   // Microsoft-Windows-D3D9
        private const ushort DxgiPresentStart = 42;
        private const ushort DxgiPresentMpoStart = 55;
        private const ushort D3D9PresentStart = 1;
        /// <summary>Teto por processo (perto de 2 h a 300 FPS): uma partida eterna não come a memória.</summary>
        internal const int MaxFramesPerProcess = 2000000;

        private const uint WnodeFlagTracedGuid = 0x00020000;
        private const uint EventTraceRealTimeMode = 0x00000100;
        private const uint ProcessTraceModeRealTime = 0x00000100;
        private const uint ProcessTraceModeEventRecord = 0x10000000;
        private const uint EventTraceControlStop = 1;
        private const uint EventControlCodeEnableProvider = 1;
        private const byte TraceLevelInformation = 4;
        private const int ErrorAlreadyExists = 183;
        private const int ErrorAccessDenied = 5;
        private static readonly ulong InvalidTraceHandle = IntPtr.Size == 8 ? 0xFFFFFFFFFFFFFFFFUL : 0x00000000FFFFFFFFUL;

        [StructLayout(LayoutKind.Sequential)]
        private struct WnodeHeader
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public long TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventTraceProperties
        {
            public WnodeHeader Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventTraceHeader
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public ulong ProcessorTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EventTrace
        {
            public EventTraceHeader Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TimeZoneInformation
        {
            public int Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName;
            public SystemTime StandardDate;
            public int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName;
            public SystemTime DaylightDate;
            public int DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TraceLogfileHeader
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public TimeZoneInformation TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void EventRecordCallback(IntPtr eventRecord);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EventTraceLogfile
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string LogFileName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EventTrace CurrentEvent;
            public TraceLogfileHeader LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public EventRecordCallback EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")]
        private static extern int StartTrace(out ulong sessionHandle, string sessionName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")]
        private static extern int ControlTrace(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern int EnableTraceEx2(ulong sessionHandle, ref Guid providerId, uint controlCode, byte level,
                                                 ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenTraceW", SetLastError = true)]
        private static extern ulong OpenTrace(ref EventTraceLogfile logfile);

        [DllImport("advapi32.dll")]
        private static extern int ProcessTrace(ulong[] handles, uint count, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern int CloseTrace(ulong traceHandle);

        private readonly object gate = new object();
        private readonly Dictionary<int, List<FrameSample>> byProcess = new Dictionary<int, List<FrameSample>>();
        private readonly Dictionary<int, long> lastPresent = new Dictionary<int, long>();
        private readonly Dictionary<int, string> names = new Dictionary<int, string>();
        private readonly long qpcStart;
        private readonly double qpcPerSecond = Stopwatch.Frequency;
        private readonly EventRecordCallback callback; // guardado aqui: o Windows chama de volta enquanto a sessão existir
        private ulong session;
        private ulong trace = InvalidTraceHandle;
        private Thread reader;

        private FrameProbe()
        {
            qpcStart = Stopwatch.GetTimestamp();
            callback = OnEvent;
        }

        /// <summary>Liga a leitura dos quadros. null se o Windows não deixar (sem a permissão) ou não der; o motivo vai em error.</summary>
        public static FrameProbe TryStart(out string error)
        {
            var probe = new FrameProbe();
            error = probe.Start();
            if (error == null) return probe;
            probe.Stop();
            return null;
        }

        private string Start()
        {
            int code = StartSession();
            if (code == ErrorAlreadyExists)
            {
                // Sobrou de um app que caiu no meio da partida: fecha a antiga e abre de novo.
                StopSession(0);
                code = StartSession();
            }
            if (code == ErrorAccessDenied) return L.T("sem permissão para ler os quadros", "no permission to read frames");
            if (code != 0) return L.T("o Windows não abriu a leitura dos quadros (código ", "Windows did not open frame reading (code ") + code + ")";

            Guid dxgi = DxgiProvider, d3d9 = D3D9Provider;
            EnableTraceEx2(session, ref dxgi, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);
            EnableTraceEx2(session, ref d3d9, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);

            var logfile = new EventTraceLogfile
            {
                LoggerName = SessionName,
                ProcessTraceMode = ProcessTraceModeRealTime | ProcessTraceModeEventRecord,
                EventRecordCallback = callback
            };
            trace = OpenTrace(ref logfile);
            if (trace == InvalidTraceHandle)
                return L.T("o Windows não abriu a leitura dos quadros (código ", "Windows did not open frame reading (code ") + Marshal.GetLastWin32Error() + ")";

            ulong handle = trace;
            reader = new Thread(() => ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero))
            {
                IsBackground = true,
                Name = "MouseModes frame X-ray"
            };
            reader.Start();
            return null;
        }

        /// <summary>Para de ler e devolve os quadros de cada processo (só os que entregaram quadros).</summary>
        public Dictionary<int, List<FrameSample>> Stop()
        {
            StopSession(session);
            session = 0;
            if (trace != InvalidTraceHandle)
            {
                CloseTrace(trace);
                trace = InvalidTraceHandle;
            }
            lock (gate) return new Dictionary<int, List<FrameSample>>(byProcess);
        }

        /// <summary>Nome dos processos que entregaram quadros (lido aos poucos, enquanto eles ainda estão abertos).</summary>
        public Dictionary<int, string> Names
        {
            get
            {
                RefreshNames();
                lock (gate) return new Dictionary<int, string>(names);
            }
        }

        /// <summary>Lê o nome de quem começou a entregar quadros desde a última vez.</summary>
        public void RefreshNames()
        {
            var pending = new List<int>();
            lock (gate)
                foreach (int pid in byProcess.Keys)
                    if (!names.ContainsKey(pid)) pending.Add(pid);
            foreach (int pid in pending)
            {
                string name = null;
                try { using (Process p = Process.GetProcessById(pid)) name = p.ProcessName; }
                catch { } // já fechou: fica sem nome e não conta como jogo
                lock (gate) if (name != null) names[pid] = name;
            }
        }

        private int StartSession()
        {
            IntPtr props = NewProperties();
            try
            {
                return StartTrace(out session, SessionName, props);
            }
            finally
            {
                Marshal.FreeHGlobal(props);
            }
        }

        private static void StopSession(ulong handle)
        {
            IntPtr props = NewProperties();
            try
            {
                ControlTrace(handle, handle == 0 ? SessionName : null, props, EventTraceControlStop);
            }
            finally
            {
                Marshal.FreeHGlobal(props);
            }
        }

        /// <summary>EVENT_TRACE_PROPERTIES com espaço para o nome da sessão logo depois, como o Windows pede.</summary>
        private static IntPtr NewProperties()
        {
            int structSize = Marshal.SizeOf(typeof(EventTraceProperties));
            int total = structSize + 1024 * sizeof(char);
            IntPtr buffer = Marshal.AllocHGlobal(total);
            for (int i = 0; i < total; i++) Marshal.WriteByte(buffer, i, 0);
            var props = new EventTraceProperties
            {
                Wnode = new WnodeHeader { BufferSize = (uint)total, Flags = WnodeFlagTracedGuid, ClientContext = 1 }, // 1 = relógio QPC
                LogFileMode = EventTraceRealTimeMode,
                FlushTimer = 1,
                LoggerNameOffset = (uint)structSize
            };
            Marshal.StructureToPtr(props, buffer, false);
            return buffer;
        }

        // EVENT_RECORD começa com EVENT_HEADER: ProcessId em 12, TimeStamp em 16, ProviderId em 24, EventDescriptor.Id em 40.
        private void OnEvent(IntPtr record)
        {
            try
            {
                ushort id = (ushort)Marshal.ReadInt16(record, 40);
                if (id != DxgiPresentStart && id != DxgiPresentMpoStart && id != D3D9PresentStart) return;
                var provider = (Guid)Marshal.PtrToStructure(record + 24, typeof(Guid));
                bool present = provider == DxgiProvider ? id == DxgiPresentStart || id == DxgiPresentMpoStart
                             : provider == D3D9Provider && id == D3D9PresentStart;
                if (!present) return;
                int pid = Marshal.ReadInt32(record, 12);
                long stamp = Marshal.ReadInt64(record, 16);
                Add(pid, stamp);
            }
            catch
            {
                // Evento fora do esperado: ignora, a leitura continua.
            }
        }

        private void Add(int pid, long stamp)
        {
            lock (gate)
            {
                long before;
                if (lastPresent.TryGetValue(pid, out before) && stamp > before)
                {
                    List<FrameSample> list;
                    if (!byProcess.TryGetValue(pid, out list)) byProcess[pid] = list = new List<FrameSample>();
                    if (list.Count < MaxFramesPerProcess)
                        list.Add(new FrameSample((stamp - qpcStart) / qpcPerSecond, (float)((stamp - before) * 1000.0 / qpcPerSecond)));
                }
                lastPresent[pid] = stamp;
            }
        }

        /// <summary>Os quadros do jogo: o processo da lista de jogos que mais entregou quadros. null se nenhum.</summary>
        internal static List<FrameSample> GameFrames(Dictionary<int, List<FrameSample>> byProcess, Dictionary<int, string> names, IList<string> games)
        {
            List<FrameSample> best = null;
            foreach (KeyValuePair<int, List<FrameSample>> pair in byProcess)
            {
                string name;
                if (!names.TryGetValue(pair.Key, out name) || !GameBooster.Matches(name, games)) continue;
                if (best == null || pair.Value.Count > best.Count) best = pair.Value;
            }
            return best;
        }
    }
}
