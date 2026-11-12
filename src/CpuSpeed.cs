using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MouseModes
{
    /// <summary>Uma leitura do processador na partida (a cada 3 s): velocidade e limite, em % da velocidade nominal.</summary>
    public struct CpuSpeedSample
    {
        public double At;          // s desde o começo da partida
        public float Performance;  // "% Processor Performance": passa de 100 com o turbo
        public float Limit;        // "% Performance Limit": abaixo de 100 = o firmware está segurando (calor, bateria)
        public float Busy;         // "% Processor Time": quanto o processador está ocupado
        public bool OnBattery;

        public CpuSpeedSample(double at, float performance, float limit, float busy, bool onBattery)
        {
            At = at;
            Performance = performance;
            Limit = limit;
            Busy = busy;
            OnBattery = onBattery;
        }
    }

    /// <summary>Um trecho da partida em que o processador ficou limitado (calor, energia ou bateria).</summary>
    public class CpuSlowdown
    {
        public double At;
        public double Seconds;
        public float LowestLimit;
        public bool OnBattery;
    }

    /// <summary>O processador durante a partida: velocidade média e quanto tempo ele ficou freado.</summary>
    public class CpuSpeedReport
    {
        public double AveragePerformance { get; set; }
        public double MeasuredSeconds { get; set; }
        public double LimitedSeconds { get; set; }
        public float LowestLimit { get; set; }
        /// <summary>Os trechos mais longos de freio, na ordem da partida.</summary>
        public List<CpuSlowdown> Worst { get; set; }
        /// <summary>Todos os trechos (para apontar a causa de uma travadinha).</summary>
        internal List<CpuSlowdown> All { get; set; }

        public bool Limited { get { return LimitedSeconds > 0; } }

        /// <summary>"em média a 112% da velocidade nominal · freado 4 min (até 62%)"</summary>
        public string Summary()
        {
            string speed = L.T("em média a ", "on average at ") + Math.Round(AveragePerformance) + L.T("% da velocidade nominal", "% of the nominal speed");
            if (!Limited) return speed + L.T(" · sem freio", " · no throttling");
            return speed + L.T(" · freado ", " · throttled for ") + Span(LimitedSeconds) +
                   L.T(" (até ", " (down to ") + Math.Round(LowestLimit) + "%)";
        }

        /// <summary>"45 s" ou "4 min" (o MatchReport.FormatDuration arredonda tudo para minutos).</summary>
        public static string Span(double seconds)
        {
            return seconds < 60 ? Math.Round(seconds) + " s" : MatchReport.FormatDuration(TimeSpan.FromSeconds(seconds));
        }

        public string VerdictText()
        {
            if (!Limited)
                return L.T("O processador não foi segurado por calor nem por energia.", "The CPU was not held back by heat or power.");
            double onBattery = 0;
            foreach (CpuSlowdown d in All) if (d.OnBattery) onBattery += d.Seconds;
            const string BatteryPt = "o Windows segura o processador na bateria para poupar carga; na tomada ele roda solto.";
            const string BatteryEn = "Windows holds the CPU back on battery to save charge; plugged in, it runs freely.";
            if (onBattery >= LimitedSeconds)
                return L.T("O freio foi na bateria: " + BatteryPt, "The throttling was on battery: " + BatteryEn);
            string heat = L.T("O processador ficou abaixo da velocidade nominal enquanto trabalhava: calor ou limite de energia (comum em notebook). " +
                              "Ajuda: base com ventilação, limpar a poeira, o modo de desempenho do fabricante.",
                              "The CPU stayed below its nominal speed while busy: heat or a power limit (common on laptops). " +
                              "It helps: a cooling stand, cleaning the dust, the manufacturer's performance mode.");
            return onBattery > 0 ? heat + L.T(" Uma parte foi na bateria: " + BatteryPt, " Part of it was on battery: " + BatteryEn) : heat;
        }

        /// <summary>"aos 12 min · 45 s a até 62% da velocidade"</summary>
        public static string Describe(CpuSlowdown s)
        {
            return L.T("aos ", "at ") + (int)(s.At / 60) + " min · " + Span(s.Seconds) + L.T(" a até ", " down to ") +
                   Math.Round(s.LowestLimit) + L.T("% da velocidade", "% speed") + (s.OnBattery ? L.T(" (na bateria)", " (on battery)") : "");
        }
    }

    public static class CpuSpeedStats
    {
        /// <summary>Limite abaixo disto conta como freio (pequenas oscilações perto de 100 são normais).</summary>
        public const float LimitedBelow = 90;

        /// <summary>
        /// Velocidade abaixo disto com o processador ocupado também conta: o limite de energia do turbo e boa parte do
        /// freio por calor não aparecem no "% Performance Limit". Parado, o processador baixa a velocidade de propósito.
        /// </summary>
        public const float SlowBelow = 85;
        public const float BusyFrom = 30;

        /// <summary>O quanto da velocidade nominal a leitura teve, se ela foi freada; null se não foi.</summary>
        internal static float? Throttled(CpuSpeedSample s)
        {
            if (s.Limit < LimitedBelow) return Math.Min(s.Limit, s.Performance);
            if (s.Performance < SlowBelow && s.Busy >= BusyFrom) return s.Performance;
            return null;
        }

        /// <summary>Menos leituras que isto (30 s) não dizem nada.</summary>
        public const int MinSamples = 10;

        public const int MaxWorst = 3;

        /// <param name="interval">Tempo entre leituras (s): cada leitura freada conta por esse tempo.</param>
        public static CpuSpeedReport Analyze(IList<CpuSpeedSample> samples, double interval = 3)
        {
            if (samples == null || samples.Count < MinSamples) return null;
            double total = 0;
            float lowest = 100;
            var all = new List<CpuSlowdown>();
            CpuSlowdown open = null;
            foreach (CpuSpeedSample s in samples)
            {
                total += s.Performance;
                float? speed = Throttled(s);
                if (speed == null)
                {
                    open = null;
                    continue;
                }
                if (speed.Value < lowest) lowest = speed.Value;
                if (open == null)
                {
                    open = new CpuSlowdown { At = s.At, LowestLimit = speed.Value, OnBattery = s.OnBattery };
                    all.Add(open);
                }
                open.Seconds += interval;
                if (speed.Value < open.LowestLimit) open.LowestLimit = speed.Value;
                open.OnBattery |= s.OnBattery;
            }

            var worst = new List<CpuSlowdown>(all);
            worst.Sort((a, b) => b.Seconds != a.Seconds ? b.Seconds.CompareTo(a.Seconds) : a.LowestLimit.CompareTo(b.LowestLimit));
            if (worst.Count > MaxWorst) worst.RemoveRange(MaxWorst, worst.Count - MaxWorst);
            worst.Sort((a, b) => a.At.CompareTo(b.At));

            double limited = 0;
            foreach (CpuSlowdown d in all) limited += d.Seconds;
            return new CpuSpeedReport
            {
                AveragePerformance = total / samples.Count,
                MeasuredSeconds = samples.Count * interval,
                LimitedSeconds = limited,
                LowestLimit = all.Count > 0 ? lowest : 100,
                Worst = worst,
                All = all
            };
        }

        /// <summary>O trecho freado que cobre o momento t (com folga de uma leitura para cada lado), ou null.</summary>
        internal static CpuSlowdown During(IList<CpuSlowdown> slowdowns, double t, double margin = 3)
        {
            if (slowdowns == null) return null;
            foreach (CpuSlowdown d in slowdowns)
                if (t >= d.At - margin && t <= d.At + d.Seconds + margin) return d;
            return null;
        }
    }

    /// <summary>
    /// Lê a velocidade do processador pelos contadores do Windows (os mesmos do Gerenciador de Tarefas e do perfmon),
    /// pelo nome em inglês para funcionar em qualquer idioma. Só lê, sem administrador.
    /// </summary>
    internal sealed class CpuSpeedProbe : IDisposable
    {
        private const uint FormatDouble = 0x200;

        [StructLayout(LayoutKind.Sequential)]
        private struct CounterValue
        {
            public uint Status;
            public double Value;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string source, IntPtr user, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        private IntPtr query, performance, limit, busy;

        private CpuSpeedProbe() { }

        /// <summary>null se o Windows não tem esses contadores (máquina virtual, contadores desligados).</summary>
        public static CpuSpeedProbe TryOpen()
        {
            var probe = new CpuSpeedProbe();
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out probe.query) != 0) return null;
                if (PdhAddEnglishCounter(probe.query, @"\Processor Information(_Total)\% Processor Performance", IntPtr.Zero, out probe.performance) != 0 ||
                    PdhAddEnglishCounter(probe.query, @"\Processor Information(_Total)\% Performance Limit", IntPtr.Zero, out probe.limit) != 0 ||
                    PdhAddEnglishCounter(probe.query, @"\Processor Information(_Total)\% Processor Time", IntPtr.Zero, out probe.busy) != 0)
                {
                    probe.Dispose();
                    return null;
                }
                PdhCollectQueryData(probe.query); // o primeiro valor de uma taxa sai na segunda coleta
                return probe;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }

        /// <summary>A leitura desde a última chamada. false se o Windows não deu um valor válido agora.</summary>
        public bool Read(out float performancePercent, out float limitPercent, out float busyPercent)
        {
            performancePercent = 0;
            limitPercent = 100;
            busyPercent = 0;
            if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) return false;
            CounterValue p, l, b;
            if (PdhGetFormattedCounterValue(performance, FormatDouble, IntPtr.Zero, out p) != 0 ||
                PdhGetFormattedCounterValue(limit, FormatDouble, IntPtr.Zero, out l) != 0 ||
                PdhGetFormattedCounterValue(busy, FormatDouble, IntPtr.Zero, out b) != 0) return false;
            performancePercent = (float)p.Value;
            limitPercent = (float)l.Value;
            busyPercent = (float)b.Value;
            return true;
        }

        public void Dispose()
        {
            if (query == IntPtr.Zero) return;
            PdhCloseQuery(query);
            query = IntPtr.Zero;
        }
    }
}
