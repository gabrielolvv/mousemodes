using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Contas do Raio-X da partida com leituras inventadas: nada lê processos de verdade.</summary>
    public class MatchReportTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
        private const long Second = TimeSpan.TicksPerSecond;
        private const long MB = 1024L * 1024;

        private static ProcessSample S(int pid, string name, long cpuSeconds, long ioMb = 0, long start = 1)
        {
            return new ProcessSample { Pid = pid, Name = name, StartTime = start, CpuTicks = cpuSeconds * Second, IoBytes = ioMb * MB };
        }

        [Fact]
        public void Finish_GivesAverageOfTheWholeProcessorAndThePeak()
        {
            // 4 núcleos, 3 leituras com 10 s entre elas. O chrome gasta 8 s de processador em cada intervalo:
            // 8 / (10 × 4) = 20% em cada um; média 20%, pico 20%.
            var stats = new MatchStats(T0, 4);
            stats.Add(T0, new[] { S(10, "chrome", 100) });
            stats.Add(T0.AddSeconds(10), new[] { S(10, "chrome", 108) });
            stats.Add(T0.AddSeconds(20), new[] { S(10, "chrome", 116) });

            MatchReport report = stats.Finish("cs2");
            Assert.Equal("cs2", report.Game);
            Assert.Equal(TimeSpan.FromSeconds(20), report.Duration);
            Culprit chrome = Assert.Single(report.Culprits);
            Assert.Equal("chrome", chrome.Name);
            Assert.Equal(20.0, chrome.AveragePercent, 3);
            Assert.Equal(20.0, chrome.PeakPercent, 3);
        }

        [Fact]
        public void Add_SumsEveryProcessWithTheSameName()
        {
            // Dois processos do chrome com 2 s cada num intervalo de 10 s, 1 núcleo: 40% juntos.
            var stats = new MatchStats(T0, 1);
            stats.Add(T0, new[] { S(1, "chrome", 0), S(2, "chrome", 0) });
            stats.Add(T0.AddSeconds(10), new[] { S(1, "chrome", 2), S(2, "chrome", 2) });

            Culprit chrome = Assert.Single(stats.Finish("game").Culprits);
            Assert.Equal(40.0, chrome.AveragePercent, 3);
            Assert.Equal(40.0, chrome.PeakPercent, 3);
        }

        [Fact]
        public void Add_IgnoresWhatHappenedBeforeTheMatchAndReusedPids()
        {
            var stats = new MatchStats(T0, 1);
            // Já tinha 5000 s de processador antes da partida: isso não conta.
            stats.Add(T0, new[] { S(1, "OneDrive", 5000) });
            // Mesmo PID, outro processo (outro início): a primeira leitura dele também não conta.
            stats.Add(T0.AddSeconds(10), new[] { S(1, "Teams", 9000, start: 2) });
            stats.Add(T0.AddSeconds(20), new[] { S(1, "Teams", 9000, start: 2) });

            Assert.Empty(stats.Finish("game").Culprits);
        }

        [Fact]
        public void Finish_ShowsHeavyDiskUsersEvenWithLittleProcessor()
        {
            var stats = new MatchStats(T0, 8);
            stats.Add(T0, new[] { S(1, "OneDrive", 0, 0), S(2, "notepad", 0, 0) });
            stats.Add(T0.AddMinutes(10), new[] { S(1, "OneDrive", 1, 2100), S(2, "notepad", 0, 1) });

            Culprit oneDrive = Assert.Single(stats.Finish("game").Culprits);
            Assert.Equal("OneDrive", oneDrive.Name);
            Assert.Equal(2100 * MB, oneDrive.IoBytes);
        }

        [Fact]
        public void Finish_KeepsOnlyTheTopFiveFromTheHeaviest()
        {
            var stats = new MatchStats(T0, 1);
            var first = new List<ProcessSample>();
            var second = new List<ProcessSample>();
            for (int i = 1; i <= 7; i++)
            {
                first.Add(S(i, "app" + i, 0));
                second.Add(S(i, "app" + i, i)); // app7 é o que mais pesa
            }
            stats.Add(T0, first);
            stats.Add(T0.AddSeconds(100), second);

            List<Culprit> culprits = stats.Finish("game").Culprits;
            Assert.Equal(MatchStats.MaxCulprits, culprits.Count);
            Assert.Equal("app7", culprits[0].Name);
            Assert.Equal("app3", culprits[4].Name);
        }

        [Fact]
        public void Finish_LeavesOutWhatBarelyUsedAnything()
        {
            var stats = new MatchStats(T0, 4);
            stats.Add(T0, new[] { S(1, "Spotify", 0) });
            stats.Add(T0.AddSeconds(100), new[] { S(1, "Spotify", 1) }); // 0,25%

            Assert.Empty(stats.Finish("game").Culprits);
        }

        [Theory]
        [InlineData(47, "47 min")]
        [InlineData(65, "1 h 05 min")]
        [InlineData(120, "2 h 00 min")]
        public void FormatDuration_UsesMinutesAndHours(int minutes, string expected)
        {
            Assert.Equal(expected, MatchReport.FormatDuration(TimeSpan.FromMinutes(minutes)));
        }

        [Fact]
        public void Describe_ShowsOnlyWhatMatters()
        {
            // Nenhum teste chama L.Init: os textos saem em português.
            Assert.Equal("média 18% · pico 61%", MatchReport.Describe(new Culprit { AveragePercent = 18.4, PeakPercent = 61 }));
            Assert.Equal("média 1,1% · pico 9% · 2,1 GB lidos/gravados",
                MatchReport.Describe(new Culprit { AveragePercent = 1.1, PeakPercent = 9, IoBytes = 2254857830 }));
            Assert.Equal("600 MB lidos/gravados", MatchReport.Describe(new Culprit { AveragePercent = 0.1, PeakPercent = 0.5, IoBytes = 600 * MB }));
        }

        [Fact]
        public void Recorder_DiscardsShortMatches()
        {
            var recorder = new MatchRecorder();
            Assert.False(recorder.Recording);
            recorder.Start("cs2");
            Assert.True(recorder.Recording);
            Assert.Null(recorder.Stop()); // acabou de começar: curta demais
            Assert.False(recorder.Recording);
        }
    }
}
