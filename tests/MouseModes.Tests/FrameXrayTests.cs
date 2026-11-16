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

        /// <summary>Uma partida de leituras a cada 3 s num processador de 16 threads; cada linha: uso em núcleos por programa.</summary>
        private static MatchReport Cores(params Dictionary<string, double>[] intervals)
        {
            var t0 = new DateTime(2026, 11, 16, 20, 0, 0, DateTimeKind.Utc);
            const long Second = TimeSpan.TicksPerSecond;
            var stats = new MatchStats(t0, 16);
            var ticks = new Dictionary<string, long>();
            var pids = new Dictionary<string, int>();
            var first = new List<ProcessSample>();
            foreach (string name in intervals[0].Keys)
            {
                pids[name] = pids.Count + 1;
                ticks[name] = 0;
                first.Add(Cpu(pids[name], name, 0));
            }
            stats.Add(t0, first);
            for (int i = 0; i < intervals.Length; i++)
            {
                var samples = new List<ProcessSample>();
                foreach (KeyValuePair<string, double> use in intervals[i])
                {
                    if (!pids.ContainsKey(use.Key)) { pids[use.Key] = pids.Count + 1; ticks[use.Key] = 0; } // abriu agora
                    else ticks[use.Key] += (long)(use.Value * 3 * Second);
                    samples.Add(Cpu(pids[use.Key], use.Key, ticks[use.Key]));
                }
                stats.Add(t0.AddSeconds(3 * (i + 1)), samples);
            }
            return stats.Finish("cs2");
        }

        private static Dictionary<string, double> Use(params object[] nameAndCores)
        {
            var d = new Dictionary<string, double>();
            for (int i = 0; i < nameAndCores.Length; i += 2) d[(string)nameAndCores[i]] = Convert.ToDouble(nameAndCores[i + 1]);
            return d;
        }

        [Fact]
        public void MatchStats_BlamesAProgramWhoseCoreUseJumps()
        {
            // 16 threads: o Updater sai de 5% de um núcleo para um núcleo inteiro (só 6% do todo) e é apontado.
            MatchReport report = Cores(Use("Updater", 0.05, "chrome", 0.3), Use("Updater", 0.05, "chrome", 0.3),
                                       Use("Updater", 0.05, "chrome", 0.3), Use("Updater", 1.0, "chrome", 0.3));
            CpuBurst burst = Assert.Single(report.CpuBursts);
            Assert.Equal("Updater", burst.Name);
            Assert.Equal(12, burst.At);
            Assert.Equal(100.0 / 16, burst.Percent, 3);
            Assert.Equal(100.0, burst.CorePercent, 3);
            Assert.Equal("nessa hora o Updater ocupava 100% de um núcleo do processador",
                         FrameStats.CauseOf(new Stutter { At = 13, Ms = 90 }, null, report.CpuBursts, null));
        }

        [Fact]
        public void MatchStats_DoesNotBlameAProgramThatIsAlwaysHeavy()
        {
            // O navegador ocupando 1,2 núcleo a partida inteira não vira causa de toda travadinha.
            MatchReport report = Cores(Use("chrome", 1.2), Use("chrome", 1.2), Use("chrome", 1.25), Use("chrome", 1.2), Use("chrome", 1.3));
            Assert.Empty(report.CpuBursts);
        }

        [Fact]
        public void MatchStats_FindsTheProgramThatJumpedEvenBehindAHeavierOne()
        {
            // O navegador pesa mais o tempo todo, mas quem subiu nessa hora foi o Updater.
            MatchReport report = Cores(Use("chrome", 1.2, "Updater", 0.02), Use("chrome", 1.2, "Updater", 0.02),
                                       Use("chrome", 1.2, "Updater", 0.02), Use("chrome", 1.2, "Updater", 0.95));
            CpuBurst burst = Assert.Single(report.CpuBursts);
            Assert.Equal("Updater", burst.Name);
            Assert.Equal(95.0, burst.CorePercent, 3);
        }

        [Fact]
        public void MatchStats_BlamesAProgramThatOpensInTheMiddleAlreadyBusy()
        {
            MatchReport report = Cores(Use("chrome", 0.3), Use("chrome", 0.3, "Defrag", 0), Use("chrome", 0.3, "Defrag", 0.9));
            CpuBurst burst = Assert.Single(report.CpuBursts);
            Assert.Equal("Defrag", burst.Name);
        }

        [Fact]
        public void DescribeCpu_SaysCoreOnlyWhenTheWholeProcessorWasNotTheProblem()
        {
            Assert.Equal("nessa hora o chrome usava 34% do processador", FrameStats.DescribeCpu(new CpuBurst(1, "chrome", 34, 544)));
            Assert.Equal("nessa hora o chrome usava 34% do processador", FrameStats.DescribeCpu(new CpuBurst(1, "chrome", 34)));
            Assert.Equal("nessa hora o Updater ocupava 95% de um núcleo do processador", FrameStats.DescribeCpu(new CpuBurst(1, "Updater", 6, 95)));
            Assert.Equal("nessa hora o Updater ocupava mais de um núcleo inteiro do processador",
                         FrameStats.DescribeCpu(new CpuBurst(1, "Updater", 9, 140)));
            Assert.Equal("nessa hora o Updater ocupava 100% de um núcleo do processador",
                         FrameStats.DescribeCpu(new CpuBurst(1, "Updater", 6.5, 104)));
        }

        [Theory]
        [InlineData(100, 5.0, 3, false, true)]    // subiu 20 vezes
        [InlineData(100, 50.0, 3, false, false)]  // só 2 vezes a média
        [InlineData(70, null, 0, true, false)]    // abaixo de 80% de um núcleo
        [InlineData(90, null, 0, true, true)]     // abriu no meio já ocupando um núcleo
        [InlineData(90, null, 0, false, false)]   // aberto desde o começo, sem média ainda
        [InlineData(90, 5.0, 1, false, false)]    // ainda sem média suficiente
        public void CoreSpike_NeedsAJumpOverTheProgramsOwnAverage(double core, double? average, int intervals, bool midMatch, bool expected)
        {
            Assert.Equal(expected, MatchStats.CoreSpike(core, average, intervals, midMatch));
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
        public void Probe_ReadsVulkanAndOpenGLFromTheDriverAndKeepsDirectXFromThePresent()
        {
            // 1 = jogo em Vulkan (só o driver); 2 = jogo em DirectX (o Present e, junto, o driver, um evento por quadro).
            var names = new Dictionary<int, string> { { 1, "doom" }, { 2, "cs2" } };
            var probe = new FrameProbe(pid => { string n; return names.TryGetValue(pid, out n) ? n : null; });
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            long frame = System.Diagnostics.Stopwatch.Frequency / 100; // 10 ms
            for (int i = 1; i <= 11; i++)
            {
                probe.Add(1, t + i * frame, FrameProbe.SourcePresentHistory);
                probe.Add(2, t + i * frame);
                probe.Add(2, t + i * frame + frame / 20, FrameProbe.SourcePresentHistory); // o driver, 0,5 ms depois
            }

            HashSet<int> fromDriver;
            Dictionary<int, List<FrameSample>> byProcess = probe.Stop(out fromDriver);

            Assert.Equal(10, byProcess[1].Count);
            Assert.Equal(10, byProcess[1][0].Ms, 3);
            Assert.Equal(10, byProcess[2].Count);
            Assert.Equal(new[] { 1 }, new List<int>(fromDriver).ToArray()); // o DirectX segue valendo para quem tem
            int gamePid;
            Assert.Same(byProcess[1], FrameProbe.GameFrames(byProcess, names, new List<string> { "doom" }, out gamePid));
            Assert.Equal(1, gamePid);
        }

        [Fact]
        public void Probe_CountsADriverFrameRepeatedOnAnotherPlaneOnlyOnce()
        {
            var probe = new FrameProbe(pid => "game");
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            long frame = System.Diagnostics.Stopwatch.Frequency / 100; // 10 ms
            long repeat = System.Diagnostics.Stopwatch.Frequency / 100000; // 0,01 ms: o mesmo flip em outro plano
            for (int i = 1; i <= 6; i++)
            {
                probe.Add(7, t + i * frame, FrameProbe.SourceFlipOverlay);
                probe.Add(7, t + i * frame + repeat, FrameProbe.SourceFlipOverlay);
            }

            List<FrameSample> frames = probe.Stop()[7];

            Assert.Equal(5, frames.Count);
            foreach (FrameSample f in frames) Assert.Equal(10, f.Ms, 3);
        }

        [Theory]
        [InlineData(FrameProbe.SourcePresentHistory)]
        [InlineData(FrameProbe.SourceFlip)]
        [InlineData(FrameProbe.SourceFlipOverlay)]
        public void Probe_KeepsEveryDriverFrameOfAnUncappedGame(int source)
        {
            // Minecraft Java sem limite, a 3000 FPS: 0,33 ms por quadro não é repetição.
            var probe = new FrameProbe(pid => "javaw");
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            long frame = System.Diagnostics.Stopwatch.Frequency / 3000;
            for (int i = 1; i <= 301; i++) probe.Add(9, t + i * frame, source);

            List<FrameSample> frames = probe.Stop()[9];

            Assert.Equal(300, frames.Count);
            Assert.InRange(frames[0].Ms, 0.32, 0.34);
        }

        [Theory]
        [InlineData(new[] { 100, 0, 0, 0 }, FrameProbe.SourceRuntime)]              // só DirectX
        [InlineData(new[] { 100, 100, 0, 0 }, FrameProbe.SourceRuntime)]            // DirectX e o driver junto: vale o DirectX
        [InlineData(new[] { 60, 100, 0, 0 }, FrameProbe.SourceRuntime)]             // o driver viu um pouco mais (cópias da janela)
        [InlineData(new[] { 3, 100, 0, 0 }, FrameProbe.SourcePresentHistory)]       // Vulkan com um Present de DirectX perdido
        [InlineData(new[] { 0, 0, 100, 0 }, FrameProbe.SourceFlip)]                 // OpenGL em tela cheia exclusiva
        [InlineData(new[] { 0, 40, 0, 100 }, FrameProbe.SourceFlipOverlay)]         // o tipo do driver com mais quadros
        [InlineData(new[] { 0, 0, 0, 0 }, FrameProbe.SourceRuntime)]                // nada
        public void PickSource_PrefersDirectXAndElseTheDriverKindWithMostFrames(int[] counts, int expected)
        {
            Assert.Equal(expected, FrameProbe.PickSource(counts));
        }

        [Fact]
        public void SourceOf_TakesOnlyTheFrameEventsOfEachProvider()
        {
            var dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
            var d3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
            Assert.Equal(FrameProbe.SourceRuntime, FrameProbe.SourceOf(dxgi, 42));
            Assert.Equal(FrameProbe.SourceRuntime, FrameProbe.SourceOf(dxgi, 55));
            Assert.Equal(FrameProbe.SourceRuntime, FrameProbe.SourceOf(d3d9, 1));
            Assert.Equal(FrameProbe.SourcePresentHistory, FrameProbe.SourceOf(FrameProbe.DriverProvider, 171));
            Assert.Equal(FrameProbe.SourceFlip, FrameProbe.SourceOf(FrameProbe.DriverProvider, 168));
            Assert.Equal(FrameProbe.SourceFlipOverlay, FrameProbe.SourceOf(FrameProbe.DriverProvider, 252));
            // O mesmo número em outro provedor não é quadro.
            Assert.Equal(-1, FrameProbe.SourceOf(dxgi, 171));
            Assert.Equal(-1, FrameProbe.SourceOf(FrameProbe.DriverProvider, 42));
            Assert.Equal(-1, FrameProbe.SourceOf(d3d9, 42));
            Assert.Equal(-1, FrameProbe.SourceOf(Guid.Empty, 1));
        }

        [Fact]
        public void DriverFilter_AsksWindowsForOnlyTheThreeFrameEvents()
        {
            // EVENT_FILTER_EVENT_ID: FilterIn = 1, reservado, quantidade 3 e os números 171, 168 e 252, em 16 bits.
            Assert.Equal(new byte[] { 1, 0, 3, 0, 171, 0, 168, 0, 252, 0 },
                         FrameProbe.EventIdFilter(new ushort[] { 171, 168, 252 }));
            Assert.Equal(new byte[] { 1, 0, 1, 0, 0x34, 0x12 }, FrameProbe.EventIdFilter(new ushort[] { 0x1234 }));

            // Base e Present no Windows 11; só Base antes. Nunca a palavra de desempenho do driver.
            Assert.Equal(0x8000001UL, FrameProbe.DriverKeywords(22000));
            Assert.Equal(0x8000001UL, FrameProbe.DriverKeywords(26200));
            Assert.Equal(0x1UL, FrameProbe.DriverKeywords(19045));
            Assert.Equal(0x1UL, FrameProbe.DriverKeywords(0));
        }

        [Fact]
        public void DriverFilter_StructuresHaveTheWindowsLayout()
        {
            Type parameters = typeof(FrameProbe.EnableTraceParameters);
            bool x64 = IntPtr.Size == 8;
            Assert.Equal(x64 ? 48 : 36, System.Runtime.InteropServices.Marshal.SizeOf(parameters));
            Assert.Equal(x64 ? 32 : 28, System.Runtime.InteropServices.Marshal.OffsetOf(parameters, "EnableFilterDesc").ToInt32());
            Assert.Equal(x64 ? 40 : 32, System.Runtime.InteropServices.Marshal.OffsetOf(parameters, "FilterDescCount").ToInt32());
            Type descriptor = typeof(FrameProbe.EventFilterDescriptor);
            Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf(descriptor));
            Assert.Equal(8, System.Runtime.InteropServices.Marshal.OffsetOf(descriptor, "Size").ToInt32());
            Assert.Equal(12, System.Runtime.InteropServices.Marshal.OffsetOf(descriptor, "Type").ToInt32());
        }

        [Fact]
        public void NoFramesReason_SaysWhenVulkanAndOpenGLCouldNotBeRead()
        {
            Assert.Contains("Vulkan", MatchRecorder.NoFramesReason(null));
            Assert.DoesNotContain("5)", MatchRecorder.NoFramesReason(null));
            string refused = MatchRecorder.NoFramesReason("código 5");
            Assert.Contains("DirectX 9–12", refused);
            Assert.EndsWith("(código 5)", refused);
        }

        [Fact]
        public void FramePermission_CommandTellsAlreadyInApartAndTreatsAlreadyOutAsSuccess()
        {
            string add = FramePermission.Command(true, "S-1-5-21-1-2-3-1001");
            string remove = FramePermission.Command(false, "S-1-5-21-1-2-3-1001");

            Assert.Contains("Add-LocalGroupMember -SID S-1-5-32-559 -Member S-1-5-21-1-2-3-1001 -ErrorAction Stop", add);
            Assert.Contains("Remove-LocalGroupMember -SID S-1-5-32-559", remove);
            // Pôr: já estar no grupo sai com 2 (não foi o app que pôs; a desinstalação não tira). Pôr de verdade sai com 0.
            Assert.Equal(2, FramePermission.AlreadyMemberExit);
            Assert.Contains("-eq 'MemberExistsException') { exit 2 }", add);
            Assert.DoesNotContain("MemberNotFoundException", add);
            // Tirar: já não estar no grupo conta como certo.
            Assert.Contains("-eq 'MemberNotFoundException') { exit 0 }", remove);
            Assert.DoesNotContain("MemberExistsException", remove);
            foreach (string command in new[] { add, remove })
            {
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
