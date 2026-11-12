using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Processador freando com leituras inventadas (a cada 3 s); só o último teste lê os contadores de verdade.</summary>
    public class CpuSpeedTests
    {
        /// <summary>Leituras a cada 3 s: velocidade 110% e limite 100%, menos nos trechos pedidos (início em s, quantas, limite).</summary>
        private static List<CpuSpeedSample> Match(int count, params object[] slow)
        {
            var list = new List<CpuSpeedSample>();
            for (int i = 0; i < count; i++)
            {
                double at = i * 3;
                float limit = 100, performance = 110;
                bool battery = false;
                for (int k = 0; k < slow.Length; k += 4)
                {
                    double from = Convert.ToDouble(slow[k]);
                    int samples = (int)slow[k + 1];
                    if (at >= from && at < from + samples * 3)
                    {
                        limit = Convert.ToSingle(slow[k + 2]);
                        performance = limit;
                        battery = (bool)slow[k + 3];
                    }
                }
                list.Add(new CpuSpeedSample(at, performance, limit, 50, battery));
            }
            return list;
        }

        [Fact]
        public void Analyze_SaysNoThrottlingWhenTheLimitStaysAt100()
        {
            CpuSpeedReport report = CpuSpeedStats.Analyze(Match(100));

            Assert.False(report.Limited);
            Assert.Equal(110, report.AveragePerformance, 3);
            Assert.Empty(report.Worst);
            Assert.EndsWith("sem freio", report.Summary());
        }

        [Fact]
        public void Analyze_GroupsSlowdownsAndKeepsTheLongest()
        {
            // 4 trechos: 30 s a 62%, 6 s a 80%, 9 s a 85% (na bateria) e 3 s a 88%; 92% não conta.
            List<CpuSpeedSample> samples = Match(200, 60.0, 10, 62f, false, 150.0, 2, 80f, false, 300.0, 3, 85f, true,
                                                 450.0, 1, 88f, false, 500.0, 5, 92f, false);

            CpuSpeedReport report = CpuSpeedStats.Analyze(samples);

            Assert.True(report.Limited);
            Assert.Equal(48, report.LimitedSeconds);
            Assert.Equal(62, report.LowestLimit);
            Assert.Equal(4, report.All.Count);
            Assert.Equal(new[] { 60.0, 150.0, 300.0 }, report.Worst.ConvertAll(w => w.At).ToArray());
            Assert.Equal(30, report.Worst[0].Seconds);
            Assert.True(report.Worst[2].OnBattery);
            Assert.Contains("Uma parte foi na bateria", report.VerdictText()); // 9 s de 48: o resto foi calor ou energia
            Assert.Equal("aos 1 min · 30 s a até 62% da velocidade", CpuSpeedReport.Describe(report.Worst[0]));
            Assert.Contains("freado 48 s (até 62%)", report.Summary());
        }

        [Fact]
        public void Throttled_CountsSlowOnlyWhileTheProcessorIsBusy()
        {
            Assert.Null(CpuSpeedStats.Throttled(new CpuSpeedSample(0, 130, 100, 100, false)));  // turbo com carga total
            Assert.Null(CpuSpeedStats.Throttled(new CpuSpeedSample(0, 74, 100, 8, false)));     // parado: baixa de propósito
            Assert.Equal(70f, CpuSpeedStats.Throttled(new CpuSpeedSample(0, 70, 100, 45, false))); // jogo rodando abaixo da nominal
            Assert.Equal(60f, CpuSpeedStats.Throttled(new CpuSpeedSample(0, 95, 60, 5, false)));   // firmware segurando, mesmo parado
        }

        [Fact]
        public void VerdictText_BlamesTheBatteryOnlyWhenAllOfItWasOnBattery()
        {
            CpuSpeedReport battery = CpuSpeedStats.Analyze(Match(100, 60.0, 10, 62f, true));
            Assert.StartsWith("O freio foi na bateria", battery.VerdictText());
            Assert.Equal("aos 1 min · 30 s a até 62% da velocidade (na bateria)", CpuSpeedReport.Describe(battery.Worst[0]));
            CpuSpeedReport heat = CpuSpeedStats.Analyze(Match(200, 0.0, 60, 70f, false));
            Assert.DoesNotContain("bateria", heat.VerdictText());
            Assert.Equal("aos 0 min · 3 min a até 70% da velocidade", CpuSpeedReport.Describe(heat.Worst[0]));
        }

        [Fact]
        public void Analyze_IsNullWithTooFewReadings()
        {
            Assert.Null(CpuSpeedStats.Analyze(null));
            Assert.Null(CpuSpeedStats.Analyze(Match(CpuSpeedStats.MinSamples - 1)));
        }

        [Theory]
        [InlineData(45, "45 s")]
        [InlineData(240, "4 min")]
        public void Span_ShowsSecondsUnderAMinute(double seconds, string text)
        {
            Assert.Equal(text, CpuSpeedReport.Span(seconds));
        }

        [Fact]
        public void CauseOf_UsesTheSlowdownWhenNoProgramStandsOut()
        {
            List<CpuSlowdown> slow = CpuSpeedStats.Analyze(Match(100, 60.0, 10, 62f, false)).All;
            var stutter = new Stutter { At = 75, Ms = 90 };
            var io = new List<IoBurst> { new IoBurst(76, "OneDrive", 300L * 1024 * 1024) };
            var cpu = new List<CpuBurst> { new CpuBurst(74, "chrome", 40) };

            Assert.Equal("nessa hora o chrome usava 40% do processador", FrameStats.CauseOf(stutter, io, cpu, null, slow));
            Assert.Equal("nessa hora o processador estava freado a até 62% da velocidade (calor ou limite de energia)",
                         FrameStats.CauseOf(stutter, io, null, null, slow));
            Assert.Contains("OneDrive", FrameStats.CauseOf(new Stutter { At = 200, Ms = 90 },
                                                            new List<IoBurst> { new IoBurst(200, "OneDrive", 300L * 1024 * 1024) }, null, null, slow));
        }

        [Fact]
        public void During_CoversTheSlowdownWithOneReadingOfMargin()
        {
            List<CpuSlowdown> slow = CpuSpeedStats.Analyze(Match(100, 60.0, 10, 62f, false)).All; // 60 s a 90 s
            Assert.NotNull(CpuSpeedStats.During(slow, 58));
            Assert.NotNull(CpuSpeedStats.During(slow, 92));
            Assert.Null(CpuSpeedStats.During(slow, 50));
            Assert.Null(CpuSpeedStats.During(null, 60));
        }

        [Fact]
        public void Probe_ReadsTheRealCountersWithoutErrors()
        {
            // Só lê os contadores do Windows deste PC; pode não haver (máquina virtual): aí não tem o que conferir.
            using (CpuSpeedProbe probe = CpuSpeedProbe.TryOpen())
            {
                if (probe == null) return;
                System.Threading.Thread.Sleep(300);
                float performance, limit, busy;
                if (!probe.Read(out performance, out limit, out busy)) return;
                Assert.InRange(performance, 0, 1000);
                Assert.InRange(limit, 0, 100.5f);
                Assert.InRange(busy, 0, 100.5f);
            }
        }
    }
}
