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
        public void ToSample_ConvertsQpcStampsToSecondsAndMs()
        {
            // Relógio QPC de 3 MHz (não 10 MHz): o quadro fica pronto aos 15 s e levou 7 ms.
            const long Start = 1000000000, PerSecond = 3000000;
            long stamp = Start + 15 * PerSecond;
            FrameSample sample;

            Assert.True(FrameProbe.ToSample(stamp, stamp - 21000, Start, PerSecond, out sample));
            Assert.Equal(15, sample.At, 6);
            Assert.Equal(7, sample.Ms, 3);
        }

        [Fact]
        public void ToSample_DropsStampsOutsideTheMatch()
        {
            const long Start = 1000000000, PerSecond = 10000000;
            FrameSample sample;

            // Carimbo convertido para data e hora (FILETIME, ~2022) no lugar do QPC: daria 13 bilhões de segundos.
            Assert.False(FrameProbe.ToSample(133000000000000000L, 133000000000000000L - 70000, Start, PerSecond, out sample));
            Assert.False(FrameProbe.ToSample(Start - PerSecond, Start - 2 * PerSecond, Start, PerSecond, out sample)); // antes do começo
            Assert.False(FrameProbe.ToSample(Start + PerSecond, Start + PerSecond, Start, PerSecond, out sample));    // não andou
        }

        [Fact]
        public void Probe_KeepsOnlyTheGamesFramesAndNothingAfterStop()
        {
            // Sem sessão do Windows: os Presents são inventados e o nome vem de um dicionário.
            var names = new Dictionary<int, string> { { 1, "cs2" }, { 2, "Discord" } };
            var probe = new FrameProbe(pid => { string n; return names.TryGetValue(pid, out n) ? n : null; });
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            long frame = System.Diagnostics.Stopwatch.Frequency / 100; // 10 ms
            for (int i = 1; i <= 5; i++)
            {
                probe.Add(1, t + i * frame);
                probe.Add(2, t + i * frame);
            }

            probe.RefreshNames(new List<string> { "cs2.exe" });
            for (int i = 6; i <= 8; i++)
            {
                probe.Add(1, t + i * frame);
                probe.Add(2, t + i * frame);
            }
            Dictionary<int, string> named = probe.Names;
            Dictionary<int, List<FrameSample>> byProcess = probe.Stop();
            probe.Add(1, t + 9 * frame); // evento atrasado depois do fim

            Assert.Equal(new[] { 1 }, new List<int>(byProcess.Keys).ToArray());
            Assert.Equal(7, byProcess[1].Count);
            Assert.Equal(10, byProcess[1][0].Ms, 3);
            Assert.Equal("Discord", named[2]);
            Assert.Same(byProcess[1], FrameProbe.GameFrames(byProcess, named, new List<string> { "cs2" }));
        }

        [Fact]
        public void FramePermission_CommandTreatsAlreadyInAndAlreadyOutAsSuccess()
        {
            string add = FramePermission.Command(true, "S-1-5-21-1-2-3-1001");
            string remove = FramePermission.Command(false, "S-1-5-21-1-2-3-1001");

            Assert.Contains("Add-LocalGroupMember -SID S-1-5-32-559 -Member S-1-5-21-1-2-3-1001 -ErrorAction Stop", add);
            Assert.Contains("Remove-LocalGroupMember -SID S-1-5-32-559", remove);
            foreach (string command in new[] { add, remove })
            {
                Assert.Contains("'MemberExistsException','MemberNotFoundException') { exit 0 }", command);
                Assert.EndsWith("exit 1 }", command); // qualquer outro erro continua sendo erro
                Assert.DoesNotContain("\"", command);  // vai dentro de -Command "..."
            }
        }

        [Fact]
        public void SessionName_IsPerWindowsAccount()
        {
            // As sessões ETW valem para o PC inteiro: cada conta usa a sua, e a limpeza não fecha a de outra conta.
            string one = FrameProbe.SessionNameFor("S-1-5-21-1-2-3-1001");
            string other = FrameProbe.SessionNameFor("S-1-5-21-1-2-3-1002");

            Assert.NotEqual(one, other);
            Assert.Equal(one, FrameProbe.SessionNameFor("S-1-5-21-1-2-3-1001"));
            Assert.StartsWith("MouseModes Frame X-ray", one);
            Assert.Equal("MouseModes Frame X-ray", FrameProbe.SessionNameFor(null));

            // A desta conta (só lê o SID; não abre sessão nenhuma) cabe no espaço que o Windows dá para o nome.
            string mine = FrameProbe.SessionName;
            Assert.NotEqual("MouseModes Frame X-ray", mine);
            Assert.True(mine.Length < 1024, mine);
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
