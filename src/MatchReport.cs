using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Uma leitura de um processo: tempo de processador e dados lidos/gravados desde que ele abriu.</summary>
    public struct ProcessSample
    {
        public int Pid;
        public string Name;
        public long StartTime;   // FILETIME: o Windows reaproveita PIDs
        public long CpuTicks;    // kernel + usuário, em ticks de 100 ns
        public long IoBytes;     // leitura + gravação: quase só disco (o tráfego de socket quase não entra nesta conta)
    }

    /// <summary>Um programa que pesou em segundo plano durante a partida.</summary>
    public class Culprit
    {
        public string Name { get; set; }
        /// <summary>Média do processador inteiro (como no Gerenciador de Tarefas), em %.</summary>
        public double AveragePercent { get; set; }
        /// <summary>Maior uso num intervalo de leitura, em %.</summary>
        public double PeakPercent { get; set; }
        public long IoBytes { get; set; }
    }

    /// <summary>Raio-X da partida: quanto durou, quem mais pesou em segundo plano e como estava a rede.</summary>
    public class MatchReport
    {
        public string Game { get; set; }
        public TimeSpan Duration { get; set; }
        public List<Culprit> Culprits { get; set; }
        /// <summary>A rede durante a partida; null se não deu para medir (ping bloqueado, partida curta).</summary>
        public NetworkReport Network { get; set; }
        /// <summary>Quem leu/gravou muitos dados e quando: serve para apontar o culpado de um pico de ping.</summary>
        internal List<IoBurst> Bursts { get; set; }

        /// <summary>"47 min" ou "1 h 05 min".</summary>
        public static string FormatDuration(TimeSpan duration)
        {
            int minutes = (int)Math.Round(duration.TotalMinutes);
            return minutes < 60 ? minutes + " min" : (minutes / 60) + " h " + (minutes % 60).ToString("00") + " min";
        }

        /// <summary>"média 18% · pico 60% · 2,1 GB lidos/gravados" (só o que pesou de verdade).</summary>
        public static string Describe(Culprit c)
        {
            var parts = new List<string>();
            if (c.AveragePercent >= 0.5) parts.Add(L.T("média ", "average ") + Percent(c.AveragePercent));
            if (c.PeakPercent >= 1) parts.Add(L.T("pico ", "peak ") + Percent(c.PeakPercent));
            if (c.IoBytes >= 100L * 1024 * 1024) parts.Add(FormatBytes(c.IoBytes) + L.T(" lidos/gravados", " read/written"));
            return string.Join(" · ", parts.ToArray());
        }

        private static string Percent(double value)
        {
            return (value < 10 ? value.ToString("0.#", Culture()) : value.ToString("0", Culture())) + "%";
        }

        /// <summary>"120 MB" ou "2,1 GB".</summary>
        internal static string FormatBytes(long bytes)
        {
            double mb = bytes / (1024.0 * 1024.0);
            return mb < 1024 ? mb.ToString("0", Culture()) + " MB" : (mb / 1024).ToString("0.0", Culture()) + " GB";
        }

        private static System.Globalization.CultureInfo Culture()
        {
            return L.IsEnglish ? System.Globalization.CultureInfo.InvariantCulture : System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        }
    }

    /// <summary>
    /// As contas do Raio-X, sem ler o Windows: recebe leituras de tempos em tempos e soma, por nome de programa,
    /// o processador e os dados que cada um usou entre uma leitura e outra.
    /// </summary>
    public class MatchStats
    {
        /// <summary>Só aparece quem passou de um destes: média, pico ou dados.</summary>
        internal const double MinAveragePercent = 2.0;
        internal const double MinPeakPercent = 25.0;
        internal const long MinIoBytes = 500L * 1024 * 1024;
        internal const int MaxCulprits = 5;
        /// <summary>Dados num intervalo de leitura (3 s) que contam como "estava usando muito" na hora de um pico.</summary>
        internal const long BurstBytes = 10L * 1024 * 1024;

        private class Total
        {
            public long Cpu;
            public long Io;
            public double Peak;
        }

        private readonly int processors;
        private readonly DateTime started;
        private DateTime last;
        private Dictionary<int, ProcessSample> previous = new Dictionary<int, ProcessSample>();
        private readonly Dictionary<string, Total> totals = new Dictionary<string, Total>(StringComparer.OrdinalIgnoreCase);
        private readonly List<IoBurst> bursts = new List<IoBurst>();

        public MatchStats(DateTime started, int processors)
        {
            this.started = started;
            this.processors = Math.Max(1, processors);
            last = started;
        }

        /// <summary>Leituras mais próximas que isso da anterior são ignoradas: o tempo de processador anda em fatias de ~16 ms.</summary>
        internal static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

        public void Add(DateTime at, IEnumerable<ProcessSample> samples)
        {
            // Checagem fora do ritmo (Ajustes salvos, troca automática ligada): um intervalo de poucos ms inflaria o pico.
            if (previous.Count > 0 && at - last < MinInterval) return;
            double interval = (at - last).Ticks;
            var current = new Dictionary<int, ProcessSample>();
            var intervalCpu = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var intervalIo = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (ProcessSample s in samples)
            {
                current[s.Pid] = s;
                ProcessSample before;
                // Só conta a diferença para a leitura anterior do mesmo processo: o que veio antes da partida não entra.
                if (!previous.TryGetValue(s.Pid, out before) || before.StartTime != s.StartTime) continue;
                long cpu = Math.Max(0, s.CpuTicks - before.CpuTicks);
                long io = Math.Max(0, s.IoBytes - before.IoBytes);

                Total total;
                if (!totals.TryGetValue(s.Name, out total)) totals[s.Name] = total = new Total();
                total.Cpu += cpu;
                total.Io += io;
                long sum;
                intervalCpu.TryGetValue(s.Name, out sum);
                intervalCpu[s.Name] = sum + cpu;
                intervalIo.TryGetValue(s.Name, out sum);
                intervalIo[s.Name] = sum + io;
            }

            // Quem mais leu/gravou neste intervalo, se foi muito.
            string busiest = null;
            long most = 0;
            foreach (KeyValuePair<string, long> pair in intervalIo)
                if (pair.Value > most) { busiest = pair.Key; most = pair.Value; }
            if (busiest != null && most >= BurstBytes) bursts.Add(new IoBurst((at - started).TotalSeconds, busiest, most));

            if (interval > 0)
            {
                foreach (KeyValuePair<string, long> pair in intervalCpu)
                {
                    double percent = pair.Value / (interval * processors) * 100.0;
                    Total total = totals[pair.Key];
                    if (percent > total.Peak) total.Peak = percent;
                }
            }
            previous = current;
            last = at;
        }

        public MatchReport Finish(string game)
        {
            TimeSpan duration = last - started;
            var culprits = new List<Culprit>();
            double available = Math.Max(1, duration.Ticks) * (double)processors;
            foreach (KeyValuePair<string, Total> pair in totals)
            {
                var c = new Culprit
                {
                    Name = pair.Key,
                    AveragePercent = pair.Value.Cpu / available * 100.0,
                    PeakPercent = Math.Min(100.0, pair.Value.Peak),
                    IoBytes = pair.Value.Io
                };
                if (c.AveragePercent >= MinAveragePercent || c.PeakPercent >= MinPeakPercent || c.IoBytes >= MinIoBytes)
                    culprits.Add(c);
            }
            // Primeiro quem mais pesou no processador; dados contam como desempate (500 MB valem 1 ponto).
            culprits.Sort((a, b) => Score(b).CompareTo(Score(a)));
            if (culprits.Count > MaxCulprits) culprits.RemoveRange(MaxCulprits, culprits.Count - MaxCulprits);
            return new MatchReport { Game = game, Duration = duration, Culprits = culprits, Bursts = new List<IoBurst>(bursts) };
        }

        private static double Score(Culprit c)
        {
            return c.AveragePercent + c.PeakPercent / 10.0 + c.IoBytes / (double)MinIoBytes;
        }
    }

    /// <summary>
    /// Lê os processos durante a partida (no Performance, com um jogo da lista aberto). Só lê: não muda nada.
    /// Abre cada processo com o acesso mínimo, sem pedir administrador; quem não deixa ler fica de fora.
    /// </summary>
    public class MatchRecorder
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll")]
        private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

        /// <summary>Partidas mais curtas que isto não geram relatório (abriu e fechou, menu, teste).</summary>
        internal static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(2);

        private readonly int self = Process.GetCurrentProcess().Id;
        private readonly bool measureNetwork;
        private MatchStats stats;
        private NetworkProbe probe;
        private string game;

        /// <param name="measureNetwork">Mede também a rede (ping a cada segundo, numa thread própria).</param>
        public MatchRecorder(bool measureNetwork = true)
        {
            this.measureNetwork = measureNetwork;
        }

        public bool Recording { get { return stats != null; } }

        public void Start(string gameName)
        {
            game = gameName;
            DateTime now = DateTime.UtcNow;
            stats = new MatchStats(now, Environment.ProcessorCount);
            probe = measureNetwork ? new NetworkProbe(now) : null;
        }

        public void Sample(IList<string> games)
        {
            if (stats == null) return;
            stats.Add(DateTime.UtcNow, Read(games));
        }

        /// <summary>Fecha a partida. null se ela foi curta demais para dizer algo.</summary>
        public MatchReport Stop()
        {
            if (stats == null) return null;
            MatchReport report = stats.Finish(game);
            stats = null;
            if (probe != null)
            {
                bool wifi, vpn;
                List<NetSample> samples = probe.Stop(out wifi, out vpn);
                probe = null;
                report.Network = NetworkStats.Analyze(samples, wifi, report.Bursts);
                if (report.Network != null) report.Network.Vpn = vpn;
            }
            return report.Duration >= MinDuration ? report : null;
        }

        /// <summary>O jogo, o próprio app e os processos do Windows ficam de fora: o relatório é sobre o que dá para mudar.</summary>
        private List<ProcessSample> Read(IList<string> games)
        {
            var list = new List<ProcessSample>();
            foreach (Process proc in Process.GetProcesses())
            {
                try
                {
                    string name = proc.ProcessName;
                    if (proc.Id == self || AppCloser.Protected.Contains(name) || GameBooster.Matches(name, games)) continue;
                    IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, proc.Id);
                    if (handle == IntPtr.Zero) continue; // acesso negado: processo de outro usuário ou protegido
                    try
                    {
                        long creation, exit, kernel, user;
                        IoCounters io;
                        if (!GetProcessTimes(handle, out creation, out exit, out kernel, out user)) continue;
                        long bytes = GetProcessIoCounters(handle, out io) ? (long)(io.ReadTransferCount + io.WriteTransferCount) : 0;
                        list.Add(new ProcessSample { Pid = proc.Id, Name = name, StartTime = creation, CpuTicks = kernel + user, IoBytes = bytes });
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
                catch { } // fechou no meio da leitura
                finally
                {
                    proc.Dispose();
                }
            }
            return list;
        }
    }
}
