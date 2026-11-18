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
        /// <summary>Do processador inteiro (todos os núcleos = 100).</summary>
        public double Percent;
        /// <summary>De um núcleo (um núcleo inteiro = 100; passa de 100 com vários). 0 = não informado.</summary>
        public double CorePercent;

        public CpuBurst(double at, string name, double percent, double corePercent = 0)
        {
            At = at;
            Name = name;
            Percent = percent;
            CorePercent = corePercent;
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
        /// <summary>
        /// Os quadros vieram do driver de vídeo (jogo em Vulkan ou OpenGL), não do Present do DirectX: a hora de cada um é a
        /// de quando o quadro chegou ao driver, um pouco menos precisa.
        /// </summary>
        public bool FromDriver { get; set; }
        /// <summary>Todas as travadinhas, em ordem de tempo (para o gráfico da partida). Não vai para o histórico.</summary>
        internal List<Stutter> All { get; set; }

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
        /// <summary>
        /// O mesmo, medido em um núcleo: quase um núcleo inteiro conta, se o programa se destacou nessa hora
        /// (MatchStats.CoreSpike). Num processador de 16 threads, um programa num núcleo inteiro é só 6% do todo.
        /// </summary>
        internal const double CpuBurstCorePercent = 80.0;

        /// <summary>Travadinha: quadro com pelo menos 2,5 × o normal e 20 ms a mais.</summary>
        internal static double Threshold(double medianMs)
        {
            return Math.Max(medianMs * 2.5, medianMs + 20);
        }

        public static FrameReport Analyze(IList<FrameSample> frames, IList<IoBurst> io, IList<CpuBurst> cpu, IList<NetSample> net,
                                          IList<CpuSlowdown> slowdowns = null)
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
            foreach (Stutter s in worst) s.Cause = CauseOf(s, io, cpu, net, slowdowns);
            worst.Sort((a, b) => a.At.CompareTo(b.At));

            return new FrameReport
            {
                Frames = used.Count,
                AverageFps = used.Count / (total / 1000.0),
                Low1Fps = LowFps(sorted, 0.01),
                Low01Fps = LowFps(sorted, 0.001),
                MedianMs = median,
                StutterCount = stutters.Count,
                Worst = worst,
                All = stutters
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

        /// <summary>
        /// "nessa hora o chrome usava 34% do processador"; ou, quando o que pesou foi um núcleo só (abaixo de 20% do
        /// todo), "nessa hora o Updater ocupava 95% de um núcleo do processador".
        /// </summary>
        internal static string DescribeCpu(CpuBurst b)
        {
            if (b.Percent >= CpuBurstPercent || b.CorePercent < CpuBurstCorePercent)
                return L.T("nessa hora o ", "meanwhile ") + b.Name + L.T(" usava ", " used ") + Math.Round(b.Percent) +
                       L.T("% do processador", "% of the CPU");
            // Até ~1 núcleo (a leitura oscila uns pontos acima de 100): "de um núcleo"; bem mais que isso, "mais de um".
            if (b.CorePercent >= 110)
                return L.T("nessa hora o ", "meanwhile ") + b.Name + L.T(" ocupava mais de um núcleo inteiro do processador",
                                                                        " kept more than one whole CPU core busy");
            return L.T("nessa hora o ", "meanwhile ") + b.Name + L.T(" ocupava ", " kept ") + Math.Round(Math.Min(100, b.CorePercent)) +
                   L.T("% de um núcleo do processador", "% of one CPU core busy");
        }

        /// <summary>O que mais se destacou perto da travadinha: processador, depois disco, depois a rede.</summary>
        internal static string CauseOf(Stutter s, IList<IoBurst> io, IList<CpuBurst> cpu, IList<NetSample> net,
                                       IList<CpuSlowdown> slowdowns = null)
        {
            CpuBurst? topCpu = null;
            if (cpu != null)
                foreach (CpuBurst b in cpu)
                    if (Math.Abs(b.At - s.At) <= CauseWindow && (topCpu == null || b.Percent > topCpu.Value.Percent)) topCpu = b;
            if (topCpu != null) return DescribeCpu(topCpu.Value);

            // Um programa pesado vem antes: é o que dá para mudar. O freio explica a travadinha quando nada mais explica.
            CpuSlowdown slow = CpuSpeedStats.During(slowdowns, s.At);
            if (slow != null)
                return L.T("nessa hora o processador estava freado a até ", "meanwhile the CPU was throttled down to ") + Math.Round(slow.LowestLimit) +
                       (slow.OnBattery ? L.T("% da velocidade (na bateria)", "% speed (on battery)")
                                       : L.T("% da velocidade (calor ou limite de energia)", "% speed (heat or power limit)"));

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

        /// <summary>
        /// Põe (ou tira) a conta do grupo. Abre o pedido de administrador do Windows. null se deu certo. alreadyMember:
        /// ao pôr, a conta já estava no grupo (posta por um administrador ou pela política do PC, não pelo app).
        /// </summary>
        public static string Change(bool allow, out bool alreadyMember)
        {
            alreadyMember = false;
            try
            {
                string user;
                using (WindowsIdentity me = WindowsIdentity.GetCurrent()) user = me.User.Value;
                var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + Command(allow, user) + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(info))
                {
                    p.WaitForExit(30000);
                    if (!p.HasExited) return L.T("o Windows não respondeu a tempo", "Windows did not answer in time");
                    // Já estava (ou já não estava) no grupo também conta como certo; ao pôr, avisa quem chamou.
                    if (allow && p.ExitCode == AlreadyMemberExit)
                    {
                        alreadyMember = true;
                        return null;
                    }
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

        /// <summary>Saída do comando de pôr quando a conta já estava no grupo (o app não pôs).</summary>
        internal const int AlreadyMemberExit = 2;

        /// <summary>
        /// O comando do PowerShell que põe (ou tira) a conta do grupo. O -ErrorAction Stop transforma "já está" e "já não
        /// está" em MemberExists/MemberNotFound. Pôr: 0 se o app pôs, AlreadyMemberExit se já estava (então não foi o
        /// app, e a desinstalação não deve tirar). Tirar: 0 se tirou ou já não estava. Qualquer outro erro sai com 1.
        /// </summary>
        internal static string Command(bool allow, string userSid)
        {
            string cmdlet = allow ? "Add" : "Remove";
            string expected = allow ? "'MemberExistsException') { exit " + AlreadyMemberExit + " }" : "'MemberNotFoundException') { exit 0 }";
            return "try { " + cmdlet + "-LocalGroupMember -SID " + PerformanceLogUsers + " -Member " + userSid +
                   " -ErrorAction Stop } catch { if ($_.Exception.GetType().Name -eq " + expected + "; exit 1 }";
        }
    }

    /// <summary>
    /// Lê, durante a partida, o momento em que cada processo entrega um quadro para a tela, por uma sessão de rastreamento
    /// do Windows (ETW) em tempo real: os eventos de Present do DirectX 9 ao 12 e, para os jogos em Vulkan ou OpenGL, que
    /// não passam por eles, os eventos de apresentação do driver de vídeo (DxgKrnl). São os mesmos que o PresentMon usa.
    /// Não entra no jogo nem mexe nele.
    /// </summary>
    public sealed class FrameProbe
    {
        private const string SessionBaseName = "MouseModes Frame X-ray";

        /// <summary>
        /// Nome da sessão no Windows. As sessões ETW valem para o PC inteiro, e o app é por conta: com o SID da conta no
        /// nome, abrir, fechar e limpar a que sobrou nunca pegam a captura de outra conta do Windows.
        /// </summary>
        internal static readonly string SessionName = SessionNameFor(CurrentUserId());

        /// <summary>O nome da sessão para uma conta (SID). Sem SID, o nome da conta; sem os dois, o nome base.</summary>
        internal static string SessionNameFor(string userId)
        {
            return string.IsNullOrEmpty(userId) ? SessionBaseName : SessionBaseName + " " + userId;
        }

        private static string CurrentUserId()
        {
            try
            {
                using (WindowsIdentity me = WindowsIdentity.GetCurrent())
                    if (me.User != null) return me.User.Value;
            }
            catch { }
            try { return Environment.UserDomainName + "\\" + Environment.UserName; }
            catch { return null; }
        }

        private static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");   // Microsoft-Windows-DXGI
        private static readonly Guid D3D9Provider = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");   // Microsoft-Windows-D3D9
        private const ushort DxgiPresentStart = 42;
        private const ushort DxgiPresentMpoStart = 55;
        private const ushort D3D9PresentStart = 1;

        /// <summary>Microsoft-Windows-DxgKrnl: o driver de vídeo. Por ele passam também os quadros de Vulkan e OpenGL.</summary>
        internal static readonly Guid DriverProvider = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");
        /// <summary>PresentHistory_Start: quadro em janela, sem borda ou em flip independente (o caminho comum hoje).</summary>
        internal const ushort DriverPresentHistory = 171;
        /// <summary>Flip_Info: quadro em tela cheia exclusiva.</summary>
        internal const ushort DriverFlip = 168;
        /// <summary>FlipMultiPlaneOverlay_Info: quadro em tela cheia por um plano de sobreposição (MPO).</summary>
        internal const ushort DriverFlipOverlay = 252;
        private static readonly ushort[] DriverEvents = { DriverPresentHistory, DriverFlip, DriverFlipOverlay };
        /// <summary>Palavras-chave Base e Present do DxgKrnl: o Windows 11 tem a Present, que estreita o que o driver gera.</summary>
        private const ulong DriverKeywordBase = 0x1;
        private const ulong DriverKeywordPresent = 0x8000000;
        private const int Windows11Build = 22000;
        private const uint EventFilterTypeEventId = 0x80000200;
        private const uint EnablePropertyIgnoreKeyword0 = 0x10;
        private const uint EnableTraceParametersVersion2 = 2;

        /// <summary>
        /// De onde veio o quadro. O Present do DirectX tem a hora exata da chamada do jogo. Os do driver chegam um pouco
        /// depois e cada tipo de apresentação gera o seu evento, então cada um fica numa lista separada: misturar os tipos
        /// contaria o mesmo quadro duas vezes.
        /// </summary>
        internal const int SourceRuntime = 0;
        internal const int SourcePresentHistory = 1;
        internal const int SourceFlip = 2;
        internal const int SourceFlipOverlay = 3;
        private const int SourceCount = 4;
        /// <summary>
        /// Dois flips do driver na mesma lista com menos que isto (ms) são o mesmo quadro, registrado uma vez por plano da
        /// tela. As cópias saem juntas; 0,05 ms é 20 mil FPS, então nenhum quadro de verdade cai aqui.
        /// </summary>
        internal const double DriverDuplicateMs = 0.05;

        /// <summary>Teto por processo (perto de 2 h a 300 FPS): uma partida eterna não come a memória.</summary>
        internal const int MaxFramesPerProcess = 2000000;

        private const uint WnodeFlagTracedGuid = 0x00020000;
        private const uint EventTraceRealTimeMode = 0x00000100;
        private const uint ProcessTraceModeRealTime = 0x00000100;
        /// <summary>Sem isso o Windows converte o TimeStamp para data e hora (FILETIME); com ele fica no relógio QPC da sessão.</summary>
        private const uint ProcessTraceModeRawTimestamp = 0x00001000;
        private const uint ProcessTraceModeEventRecord = 0x10000000;
        /// <summary>Quanto esperar a thread de leitura terminar depois de fechar a sessão.</summary>
        private const int ReaderJoinMs = 3000;
        /// <summary>Quadro fora disto (antes do começo ou mais de um dia depois) é relógio errado, não jogo: fica de fora.</summary>
        internal const double MaxSampleSeconds = 24 * 60 * 60;
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

        /// <summary>ENABLE_TRACE_PARAMETERS (versão 2): só para passar o filtro de eventos do driver.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct EnableTraceParameters
        {
            public uint Version;
            public uint EnableProperty;
            public uint ControlFlags;
            public Guid SourceId;
            public IntPtr EnableFilterDesc;
            public uint FilterDescCount;
        }

        /// <summary>EVENT_FILTER_DESCRIPTOR: o Ptr é sempre de 64 bits, mesmo no app de 32.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct EventFilterDescriptor
        {
            public ulong Ptr;
            public uint Size;
            public uint Type;
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

        /// <summary>Os quadros de um processo vindos de uma fonte e o carimbo do último evento dela.</summary>
        private sealed class Track
        {
            public List<FrameSample> Frames;
            public long Last;
            public bool HasLast;
        }

        private readonly object gate = new object();
        /// <summary>Por processo, uma lista por fonte (índice SourceRuntime, SourcePresentHistory...); null = nada dela.</summary>
        private readonly Dictionary<int, Track[]> tracks = new Dictionary<int, Track[]>();
        private readonly Dictionary<int, string> names = new Dictionary<int, string>();
        /// <summary>Processos que já têm nome e não são da lista de jogos: os quadros deles não são guardados.</summary>
        private readonly HashSet<int> ignored = new HashSet<int>();
        private readonly Func<int, string> nameOf;
        private readonly long qpcStart;
        private readonly double qpcPerSecond = Stopwatch.Frequency;
        private readonly EventRecordCallback callback; // guardado aqui: o Windows chama de volta enquanto a sessão existir
        private ulong session;
        private ulong trace = InvalidTraceHandle;
        private Thread reader;
        private bool stopped;

        private FrameProbe() : this(ProcessName) { }

        /// <param name="nameOf">Nome do processo pelo PID (null se já fechou); os testes passam um inventado.</param>
        internal FrameProbe(Func<int, string> nameOf)
        {
            this.nameOf = nameOf;
            qpcStart = Stopwatch.GetTimestamp();
            callback = OnEvent;
        }

        private static string ProcessName(int pid)
        {
            try { using (Process p = Process.GetProcessById(pid)) return p.ProcessName; }
            catch { return null; } // já fechou: fica sem nome e não conta como jogo
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
                StopLeftover();
                code = StartSession();
            }
            if (code == ErrorAccessDenied) return L.T("sem permissão para ler os quadros", "no permission to read frames");
            if (code != 0) return L.T("o Windows não abriu a leitura dos quadros (código ", "Windows did not open frame reading (code ") + code + ")";

            Guid dxgi = DxgiProvider, d3d9 = D3D9Provider;
            int dxgiCode = EnableTraceEx2(session, ref dxgi, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);
            int d3d9Code = EnableTraceEx2(session, ref d3d9, EventControlCodeEnableProvider, TraceLevelInformation, ulong.MaxValue, 0, 0, IntPtr.Zero);
            // O driver é extra: se o Windows não deixar, o DirectX continua sendo lido e só Vulkan e OpenGL ficam de fora.
            DriverError = EnableDriver(session, WindowsChecks.WindowsBuild());
            if (DriverError != null)
            {
                if (dxgiCode != 0 && d3d9Code != 0)
                    return L.T("o Windows não ligou a leitura dos quadros (código ", "Windows did not turn on frame reading (code ") + dxgiCode + ")";
                Log.Warn(L.T("Caça-travadinhas: quadros de Vulkan e OpenGL não serão lidos (", "Stutter finder: Vulkan and OpenGL frames will not be read (") +
                         DriverError + ")");
            }

            var logfile = new EventTraceLogfile
            {
                LoggerName = SessionName,
                // Carimbo cru: com o ClientContext = 1 da sessão, o TimeStamp chega no relógio QPC, o mesmo do Stopwatch.
                ProcessTraceMode = ProcessTraceModeRealTime | ProcessTraceModeRawTimestamp | ProcessTraceModeEventRecord,
                EventRecordCallback = callback
            };
            trace = OpenTrace(ref logfile);
            if (trace == InvalidTraceHandle)
                return L.T("o Windows não abriu a leitura dos quadros (código ", "Windows did not open frame reading (code ") + Marshal.GetLastWin32Error() + ")";

            ulong handle = trace;
            reader = new Thread(() =>
            {
                ProcessTrace(new[] { handle }, 1, IntPtr.Zero, IntPtr.Zero);
                GC.KeepAlive(callback); // o Windows chama o callback até o ProcessTrace voltar
            })
            {
                IsBackground = true,
                Name = "MouseModes frame X-ray"
            };
            reader.Start();
            return null;
        }

        /// <summary>Por que os quadros de Vulkan e OpenGL não estão sendo lidos (o Windows recusou o driver); null se estão.</summary>
        public string DriverError { get; private set; }

        /// <summary>
        /// Liga os eventos de apresentação do driver de vídeo, só os três que interessam (filtro por número do evento),
        /// do jeito do PresentMon: sem o filtro, o driver despeja milhares de eventos por segundo. null se ligou.
        /// </summary>
        private static string EnableDriver(ulong session, int build)
        {
            byte[] filter = EventIdFilter(DriverEvents);
            IntPtr ids = Marshal.AllocHGlobal(filter.Length);
            IntPtr descriptor = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(EventFilterDescriptor)));
            IntPtr parameters = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(EnableTraceParameters)));
            try
            {
                Marshal.Copy(filter, 0, ids, filter.Length);
                Marshal.StructureToPtr(new EventFilterDescriptor
                {
                    Ptr = IntPtr.Size == 8 ? (ulong)ids.ToInt64() : (uint)ids.ToInt32(), // sem estender o sinal no app de 32 bits
                    Size = (uint)filter.Length,
                    Type = EventFilterTypeEventId
                }, descriptor, false);
                Marshal.StructureToPtr(new EnableTraceParameters
                {
                    Version = EnableTraceParametersVersion2,
                    EnableProperty = EnablePropertyIgnoreKeyword0,
                    EnableFilterDesc = descriptor,
                    FilterDescCount = 1
                }, parameters, false);
                Guid provider = DriverProvider;
                ulong keywords = DriverKeywords(build);
                int code = EnableTraceEx2(session, ref provider, EventControlCodeEnableProvider, TraceLevelInformation, keywords, keywords, 0, parameters);
                return code == 0 ? null : L.T("o Windows recusou o driver de vídeo, código ", "Windows refused the graphics driver, code ") + code;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                Marshal.FreeHGlobal(parameters);
                Marshal.FreeHGlobal(descriptor);
                Marshal.FreeHGlobal(ids);
            }
        }

        /// <summary>
        /// As palavras-chave do DxgKrnl, iguais nas duas máscaras (qualquer e todas): Base e Present no Windows 11, só Base
        /// antes dele (o Windows 10 não tem a Present). Nunca a de desempenho (0x4000...), que pesa no driver.
        /// </summary>
        internal static ulong DriverKeywords(int build)
        {
            return build >= Windows11Build ? DriverKeywordBase | DriverKeywordPresent : DriverKeywordBase;
        }

        /// <summary>EVENT_FILTER_EVENT_ID: FilterIn = 1 (só estes), um byte reservado, a quantidade e os números, em 16 bits.</summary>
        internal static byte[] EventIdFilter(ushort[] events)
        {
            var bytes = new byte[4 + 2 * events.Length];
            bytes[0] = 1;
            bytes[2] = (byte)events.Length;
            bytes[3] = (byte)(events.Length >> 8);
            for (int i = 0; i < events.Length; i++)
            {
                bytes[4 + 2 * i] = (byte)events[i];
                bytes[5 + 2 * i] = (byte)(events[i] >> 8);
            }
            return bytes;
        }

        /// <summary>
        /// Para de ler e devolve os quadros de cada processo (só os que entregaram quadros). Depois disso nada mais é
        /// acrescentado às listas devolvidas, mesmo que o Windows ainda entregue um evento atrasado.
        /// </summary>
        public Dictionary<int, List<FrameSample>> Stop()
        {
            HashSet<int> fromDriver;
            return Stop(out fromDriver);
        }

        /// <summary>Como Stop(), dizendo também quais processos tiveram os quadros lidos do driver (Vulkan, OpenGL).</summary>
        public Dictionary<int, List<FrameSample>> Stop(out HashSet<int> fromDriver)
        {
            lock (gate) stopped = true;
            if (session != 0) StopSession(session);
            session = 0;
            if (trace != InvalidTraceHandle)
            {
                CloseTrace(trace);
                trace = InvalidTraceHandle;
            }
            // Espera (pouco) a thread de leitura sair do ProcessTrace: só então o callback pode ser liberado.
            if (reader != null && reader != Thread.CurrentThread) reader.Join(ReaderJoinMs);
            reader = null;
            GC.KeepAlive(callback);
            var result = new Dictionary<int, List<FrameSample>>();
            fromDriver = new HashSet<int>();
            lock (gate)
            {
                var counts = new int[SourceCount];
                foreach (KeyValuePair<int, Track[]> pair in tracks)
                {
                    for (int s = 0; s < SourceCount; s++)
                        counts[s] = pair.Value[s] != null && pair.Value[s].Frames != null ? pair.Value[s].Frames.Count : 0;
                    int source = PickSource(counts);
                    if (counts[source] == 0) continue;
                    result[pair.Key] = pair.Value[source].Frames;
                    if (source != SourceRuntime) fromDriver.Add(pair.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Qual lista de um processo vale, pela quantidade de quadros de cada fonte. O jogo em DirectX gera também os
        /// eventos do driver, mais ou menos um por quadro: com o DirectX tendo pelo menos metade do que o driver viu, vale
        /// o DirectX, que tem a hora exata. Sem ele (Vulkan, OpenGL), vale o tipo de evento do driver com mais quadros.
        /// </summary>
        internal static int PickSource(int[] counts)
        {
            int driver = -1;
            for (int s = SourceRuntime + 1; s < counts.Length; s++)
                if (counts[s] > 0 && (driver < 0 || counts[s] > counts[driver])) driver = s;
            if (driver < 0) return SourceRuntime;
            return counts[SourceRuntime] * 2L >= counts[driver] ? SourceRuntime : driver;
        }

        /// <summary>
        /// Fecha a sessão desta conta que tenha sobrado (o app caiu no meio de uma partida), pelo nome (SessionName).
        /// Não ter sessão aberta ou não ter permissão para fechá-la não é erro: não faz nada.
        /// </summary>
        internal static void StopLeftover()
        {
            try
            {
                StopSession(0);
            }
            catch
            {
                // Sem o advapi32 (não acontece no Windows): não há sessão para fechar.
            }
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

        /// <summary>
        /// Lê o nome de quem começou a entregar quadros desde a última vez. Com a lista de jogos, quem ganhou nome e não é
        /// jogo (Discord, navegador, a área de trabalho) deixa de ter os quadros guardados até o fim da partida.
        /// </summary>
        public void RefreshNames(IList<string> games = null)
        {
            var pending = new List<int>();
            lock (gate)
                foreach (int pid in tracks.Keys)
                    if (!names.ContainsKey(pid)) pending.Add(pid);
            foreach (int pid in pending)
            {
                string name = nameOf(pid);
                if (name == null) continue;
                lock (gate)
                {
                    names[pid] = name;
                    if (games != null && !GameBooster.Matches(name, games))
                    {
                        tracks.Remove(pid);
                        ignored.Add(pid);
                    }
                }
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
                if (id != DxgiPresentStart && id != DxgiPresentMpoStart && id != D3D9PresentStart &&
                    id != DriverPresentHistory && id != DriverFlip && id != DriverFlipOverlay) return;
                var provider = (Guid)Marshal.PtrToStructure(record + 24, typeof(Guid));
                int source = SourceOf(provider, id);
                if (source < 0) return;
                int pid = Marshal.ReadInt32(record, 12);
                long stamp = Marshal.ReadInt64(record, 16);
                Add(pid, stamp, source);
            }
            catch
            {
                // Evento fora do esperado: ignora, a leitura continua.
            }
        }

        /// <summary>A fonte de um evento (SourceRuntime, SourcePresentHistory...), ou -1 se não é um quadro.</summary>
        internal static int SourceOf(Guid provider, ushort id)
        {
            if (provider == DxgiProvider) return id == DxgiPresentStart || id == DxgiPresentMpoStart ? SourceRuntime : -1;
            if (provider == D3D9Provider) return id == D3D9PresentStart ? SourceRuntime : -1;
            if (provider == DriverProvider)
            {
                if (id == DriverPresentHistory) return SourcePresentHistory;
                if (id == DriverFlip) return SourceFlip;
                if (id == DriverFlipOverlay) return SourceFlipOverlay;
            }
            return -1;
        }

        /// <summary>Um quadro do processo, com o carimbo no relógio QPC e a fonte (por padrão, o Present do DirectX).</summary>
        internal void Add(int pid, long stamp, int source = SourceRuntime)
        {
            lock (gate)
            {
                if (stopped || ignored.Contains(pid)) return;
                Track[] mine;
                if (!tracks.TryGetValue(pid, out mine)) tracks[pid] = mine = new Track[SourceCount];
                Track track = mine[source] ?? (mine[source] = new Track());
                if (track.HasLast)
                {
                    // O driver pode registrar o mesmo flip mais de uma vez (um por plano da tela): colado no anterior é
                    // repetição, não quadro novo. O PresentHistory não repete, e o Present do DirectX nunca.
                    bool flip = source == SourceFlip || source == SourceFlipOverlay;
                    if (flip && stamp > track.Last && (stamp - track.Last) * 1000.0 / qpcPerSecond < DriverDuplicateMs) return;
                    FrameSample sample;
                    if (ToSample(stamp, track.Last, qpcStart, qpcPerSecond, out sample))
                    {
                        if (track.Frames == null) track.Frames = new List<FrameSample>();
                        if (track.Frames.Count < MaxFramesPerProcess) track.Frames.Add(sample);
                    }
                }
                track.Last = stamp;
                track.HasLast = true;
            }
        }

        /// <summary>
        /// O quadro entre dois Presents (carimbos QPC): quando ficou pronto, em s desde start, e quanto levou, em ms.
        /// false se não andou para frente ou se caiu fora da partida (relógio errado): melhor sem o quadro que com um absurdo.
        /// </summary>
        internal static bool ToSample(long stamp, long before, long start, double perSecond, out FrameSample sample)
        {
            sample = default(FrameSample);
            if (stamp <= before || perSecond <= 0) return false;
            double at = (stamp - start) / perSecond;
            if (at < 0 || at > MaxSampleSeconds) return false;
            sample = new FrameSample(at, (float)((stamp - before) * 1000.0 / perSecond));
            return true;
        }

        /// <summary>Os quadros do jogo: o processo da lista de jogos que mais entregou quadros. null se nenhum.</summary>
        internal static List<FrameSample> GameFrames(Dictionary<int, List<FrameSample>> byProcess, Dictionary<int, string> names, IList<string> games)
        {
            int pid;
            return GameFrames(byProcess, names, games, out pid);
        }

        /// <summary>Como o outro, dizendo também de qual processo são os quadros (0 se nenhum).</summary>
        internal static List<FrameSample> GameFrames(Dictionary<int, List<FrameSample>> byProcess, Dictionary<int, string> names, IList<string> games,
                                                     out int pid)
        {
            List<FrameSample> best = null;
            pid = 0;
            foreach (KeyValuePair<int, List<FrameSample>> pair in byProcess)
            {
                string name;
                if (!names.TryGetValue(pair.Key, out name) || !GameBooster.Matches(name, games)) continue;
                if (best == null || pair.Value.Count > best.Count)
                {
                    best = pair.Value;
                    pid = pair.Key;
                }
            }
            return best;
        }
    }
}
