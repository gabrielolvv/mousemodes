using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Caça-travadinhas com quadros inventados: nada aqui liga o rastreamento do Windows.</summary>
    public class FrameXrayTests
    {
        /// <summary>Uma partida a FPS constante (quadros de ms cada), começando depois do aquecimento.</summary>
        private static List<FrameSample> Steady(double seconds, float ms, double from = FrameStats.WarmupSeconds)
        {
            var list = new List<FrameSample>();
            for (double t = from; t < from + seconds; t += ms / 1000.0) list.Add(new FrameSample(t, ms));
            return list;
        }

        [Fact]
        public void Analyze_GivesAverageAndLowsOfASteadyMatch()
        {
            FrameReport report = FrameStats.Analyze(Steady(60, 10f), null, null, null); // 100 FPS cravados

            Assert.Equal(100, report.AverageFps, 1);
            Assert.Equal(100, report.Low1Fps, 1);
            Assert.Equal(0, report.StutterCount);
            Assert.Empty(report.Worst);
        }

        [Fact]
        public void Analyze_FindsStuttersAndTheOnePercentLowDrops()
        {
            List<FrameSample> frames = Steady(60, 7f); // ~143 FPS
            frames.Add(new FrameSample(30.0, 85f));    // travada de 85 ms
            frames.Add(new FrameSample(30.2, 60f));    // mesma travadinha (menos de 1 s depois)
            frames.Add(new FrameSample(50.0, 40f));    // outra
            frames.Sort((a, b) => a.At.CompareTo(b.At));

            FrameReport report = FrameStats.Analyze(frames, null, null, null);

            Assert.Equal(2, report.StutterCount);
            Assert.Equal(new[] { 30.0, 50.0 }, report.Worst.ConvertAll(s => s.At).ToArray());
            Assert.Equal(85, report.Worst[0].Ms);
            Assert.True(report.Low1Fps < report.AverageFps);
            Assert.True(report.Low01Fps <= report.Low1Fps);
        }

        [Fact]
        public void Analyze_IgnoresPausesAndTheLoadingScreen()
        {
            List<FrameSample> frames = Steady(60, 10f);
            frames.Add(new FrameSample(2.0, 400f));   // carregando, antes do aquecimento acabar
            frames.Add(new FrameSample(40.0, 5000f)); // alt-tab: o jogo parou de entregar quadros
            frames.Sort((a, b) => a.At.CompareTo(b.At));

            FrameReport report = FrameStats.Analyze(frames, null, null, null);

            Assert.Equal(0, report.StutterCount);
            Assert.Equal(100, report.AverageFps, 1);
        }

        [Fact]
        public void Analyze_IsNullWithoutEnoughFrames()
        {
            Assert.Null(FrameStats.Analyze(null, null, null, null));
            Assert.Null(FrameStats.Analyze(Steady(1, 10f), null, null, null)); // 100 quadros
            Assert.Null(FrameStats.Analyze(Steady(60, 10f, from: 0).FindAll(f => f.At < FrameStats.WarmupSeconds), null, null, null));
        }

        [Fact]
        public void Threshold_IsTwoAndAHalfTimesTheNormalAndAtLeastTwentyMsMore()
        {
            Assert.Equal(27, FrameStats.Threshold(7), 6);    // 144 FPS: 7 × 2,5 = 17,5 < 7 + 20
            Assert.Equal(83.25, FrameStats.Threshold(33.3), 6); // 30 FPS: 33,3 × 2,5
        }

        [Fact]
        public void CauseOf_PrefersTheCpuThenTheDiskThenTheNetwork()
        {
            var stutter = new Stutter { At = 100, Ms = 90 };
            var cpu = new List<CpuBurst> { new CpuBurst(99, "chrome", 34), new CpuBurst(10, "steam", 80) };
            var io = new List<IoBurst> { new IoBurst(101, "OneDrive", 300L * 1024 * 1024) };
            var net = new List<NetSample> { new NetSample(100, 2, NetSample.Lost) };

            Assert.Equal("nessa hora o chrome usava 34% do processador", FrameStats.CauseOf(stutter, io, cpu, net));
            Assert.Equal("nessa hora o OneDrive lia/gravava 300 MB", FrameStats.CauseOf(stutter, io, null, net));
            Assert.Contains("internet", FrameStats.CauseOf(stutter, null, null, net));
            Assert.Null(FrameStats.CauseOf(stutter, null, cpu.FindAll(b => b.At < 50), null)); // longe da travadinha
        }

        [Fact]
        public void Describe_SaysWhenNothingStoodOut()
        {
            string text = FrameReport.Describe(new Stutter { At = 23 * 60 + 5, Ms = 84.6 });
            Assert.StartsWith("aos 23 min · quadro de 85 ms · nada em segundo plano se destacou", text);
        }

        [Fact]
        public void GameFrames_PicksTheListedGameThatDeliveredMostFrames()
        {
            var byProcess = new Dictionary<int, List<FrameSample>>
            {
                { 1, Steady(10, 10f) },   // cs2
                { 2, Steady(10, 5f) },    // Discord (overlay também entrega quadros)
                { 3, Steady(2, 10f) },    // cs2 de novo (processo curto)
                { 4, Steady(10, 10f) }    // já fechou: sem nome
            };
            var names = new Dictionary<int, string> { { 1, "cs2" }, { 2, "Discord" }, { 3, "cs2" } };

            List<FrameSample> game = FrameProbe.GameFrames(byProcess, names, new List<string> { "cs2" });

            Assert.Same(byProcess[1], game);
            Assert.Null(FrameProbe.GameFrames(byProcess, names, new List<string> { "valorant" }));
        }

        [Fact]
        public void MatchStats_RemembersWhoUsedALotOfCpuAndWhen()
        {
            var t0 = new DateTime(2026, 10, 30, 20, 0, 0, DateTimeKind.Utc);
            const long Second = TimeSpan.TicksPerSecond;
            var stats = new MatchStats(t0, 1);
            stats.Add(t0, new[] { Cpu(1, "chrome", 0), Cpu(2, "steam", 0) });
            stats.Add(t0.AddSeconds(3), new[] { Cpu(1, "chrome", Second), Cpu(2, "steam", Second / 10) });           // chrome 33%
            stats.Add(t0.AddSeconds(6), new[] { Cpu(1, "chrome", Second + Second / 10), Cpu(2, "steam", Second / 5) }); // ninguém a 20%

            CpuBurst burst = Assert.Single(stats.Finish("cs2").CpuBursts);
            Assert.Equal("chrome", burst.Name);
            Assert.Equal(3, burst.At);
            Assert.Equal(100.0 / 3, burst.Percent, 3);
        }

        [Fact]
        public void FramePermission_IsReadFromTheTokenWithoutErrors()
        {
            // Só lê o token desta sessão (o grupo e o administrador); não muda nada no Windows.
            Assert.Null(Record.Exception(() => { bool allowed = FramePermission.Allowed; GC.KeepAlive(allowed); }));
        }

        private static ProcessSample Cpu(int pid, string name, long ticks)
        {
            return new ProcessSample { Pid = pid, Name = name, StartTime = 1, CpuTicks = ticks, IoBytes = 0 };
        }
    }
}
