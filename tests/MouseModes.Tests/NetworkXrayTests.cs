using System;
using System.Collections.Generic;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>Contas do Raio-X da rede com pings inventados: nada aqui manda ping de verdade.</summary>
    public class NetworkXrayTests
    {
        private const int Lost = NetSample.Lost;

        /// <summary>Uma partida de N segundos com ping estável: 20 ms à internet e 2 ms ao roteador.</summary>
        private static List<NetSample> Calm(int seconds, int internet = 20, int gateway = 2)
        {
            var list = new List<NetSample>();
            // Variação de 1 ms para cima e para baixo, como numa rede de verdade (perda continua perda).
            for (int i = 0; i < seconds; i++) list.Add(new NetSample(i, gateway, internet < 0 ? internet : internet + (i % 3) - 1));
            return list;
        }

        private static void Set(List<NetSample> samples, int from, int to, int gateway, int internet)
        {
            for (int i = from; i <= to; i++) samples[i] = new NetSample(i, gateway, internet);
        }

        [Fact]
        public void Analyze_CallsAStableNetworkStable()
        {
            NetworkReport report = NetworkStats.Analyze(Calm(120), false, null);

            Assert.Equal(NetVerdict.Stable, report.Verdict);
            Assert.Equal(0, report.SpikeCount);
            Assert.Equal(20, report.InternetMedian);
            Assert.Equal(2, report.GatewayMedian);
            Assert.Equal(0, report.LossPercent);
            Assert.True(report.Jitter < 2);
        }

        [Fact]
        public void Analyze_BlamesTheInternetWhenTheRouterStaysFine()
        {
            List<NetSample> samples = Calm(400);
            Set(samples, 100, 105, 2, 150);
            Set(samples, 300, 302, 3, 180);

            NetworkReport report = NetworkStats.Analyze(samples, false, null);

            Assert.Equal(NetVerdict.Internet, report.Verdict);
            Assert.Equal(2, report.SpikeCount);
            NetSpike first = report.Spikes[0];
            Assert.Equal(100, first.At);
            Assert.Equal(6, first.Seconds);
            Assert.Equal(150, first.PeakMs);
            Assert.False(first.Local);
        }

        [Fact]
        public void Analyze_BlamesTheHomeNetworkWhenTheRouterSpikesToo()
        {
            List<NetSample> samples = Calm(400);
            Set(samples, 100, 104, 90, 160);
            Set(samples, 250, 251, Lost, Lost);

            NetworkReport report = NetworkStats.Analyze(samples, true, null);

            Assert.Equal(NetVerdict.Local, report.Verdict);
            Assert.True(report.Spikes[0].Local);
            Assert.True(report.Wifi);
            Assert.Contains("Wi-Fi", report.VerdictText());
        }

        [Fact]
        public void Analyze_LossOnlyBeyondTheRouterIsTheInternet()
        {
            // Pacotes perdidos soltos (sem pico) só depois do roteador: 3 de 120 = 2,5% de perda.
            List<NetSample> samples = Calm(120);
            foreach (int i in new[] { 10, 50, 90 }) samples[i] = new NetSample(i, 2, Lost);

            NetworkReport report = NetworkStats.Analyze(samples, false, null);

            Assert.Equal(0, report.SpikeCount); // um pacote perdido sozinho não é pico
            Assert.Equal(2.5, report.LossPercent, 6);
            Assert.Equal(NetVerdict.Internet, report.Verdict);
        }

        [Fact]
        public void Analyze_ASingleHiccupIsStillStable()
        {
            List<NetSample> samples = Calm(120);
            samples[60] = new NetSample(60, 2, 140);

            NetworkReport report = NetworkStats.Analyze(samples, false, null);

            Assert.Equal(1, report.SpikeCount);
            Assert.Equal(NetVerdict.Stable, report.Verdict);
        }

        [Fact]
        public void Analyze_ASingleLongOutageIsAProblem()
        {
            // Uma hora de partida com uma queda de 30 s: um pico só e menos de 1% de perda, mas não é "estável".
            List<NetSample> samples = Calm(3600);
            Set(samples, 1800, 1829, Lost, Lost);

            NetworkReport report = NetworkStats.Analyze(samples, true, null);

            Assert.Equal(1, report.SpikeCount);
            Assert.True(report.LossPercent < 1);
            Assert.Equal(30, report.Spikes[0].Seconds);
            Assert.Equal(NetVerdict.Local, report.Verdict);
        }

        [Fact]
        public void Analyze_CannotTellWithoutARouterThatAnswersPing()
        {
            List<NetSample> samples = Calm(400, gateway: Lost);
            Set(samples, 100, 105, Lost, 150);
            Set(samples, 300, 302, Lost, 180);

            NetworkReport report = NetworkStats.Analyze(samples, false, null);

            Assert.Equal(-1, report.GatewayMedian);
            Assert.Equal(NetVerdict.Unknown, report.Verdict);
            Assert.False(report.Spikes[0].Local);
        }

        [Fact]
        public void Analyze_IsNullWhenThereIsNothingToSay()
        {
            Assert.Null(NetworkStats.Analyze(null, false, null));
            Assert.Null(NetworkStats.Analyze(Calm(NetworkStats.MinSamples - 1), false, null));
            Assert.Null(NetworkStats.Analyze(Calm(120, internet: Lost), false, null)); // ping bloqueado
        }

        [Fact]
        public void Analyze_PointsAtWhoWasUsingDataDuringTheSpike()
        {
            List<NetSample> samples = Calm(400);
            Set(samples, 100, 105, 2, 150);
            var bursts = new List<IoBurst>
            {
                new IoBurst(30, "chrome", 400L * 1024 * 1024),     // longe do pico: não conta
                new IoBurst(102, "OneDrive", 120L * 1024 * 1024),
                new IoBurst(104, "steam", 40L * 1024 * 1024)       // perto, mas usou menos
            };

            NetSpike spike = NetworkStats.Analyze(samples, false, bursts).Spikes[0];

            Assert.Equal("OneDrive", spike.Busy);
            Assert.Equal("aos 1 min · 150 ms por 6 s · nessa hora o OneDrive lia/gravava 120 MB", NetworkReport.Describe(spike));
        }

        [Fact]
        public void Analyze_KeepsTheWorstSpikesInTimeOrder()
        {
            List<NetSample> samples = Calm(600);
            Set(samples, 50, 51, 2, 120);
            Set(samples, 150, 152, 2, 400);
            Set(samples, 250, 251, 2, 130);
            Set(samples, 350, 353, Lost, Lost);
            Set(samples, 450, 451, 2, 300);

            NetworkReport report = NetworkStats.Analyze(samples, false, null);

            Assert.Equal(5, report.SpikeCount);
            Assert.Equal(NetworkStats.MaxSpikes, report.Spikes.Count);
            Assert.Equal(new[] { 150.0, 350.0, 450.0 }, report.Spikes.ConvertAll(s => s.At).ToArray());
        }

        [Fact]
        public void MatchStats_RemembersWhoUsedALotOfDataAndWhen()
        {
            var t0 = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
            const long MB = 1024L * 1024;
            var stats = new MatchStats(t0, 4);
            stats.Add(t0, new[] { Sample(1, "OneDrive", 0), Sample(2, "chrome", 0) });
            stats.Add(t0.AddSeconds(3), new[] { Sample(1, "OneDrive", 60 * MB), Sample(2, "chrome", 2 * MB) });
            stats.Add(t0.AddSeconds(6), new[] { Sample(1, "OneDrive", 61 * MB), Sample(2, "chrome", 3 * MB) });

            List<IoBurst> bursts = stats.Finish("cs2").Bursts;

            IoBurst burst = Assert.Single(bursts); // o segundo intervalo teve pouco dado: não conta
            Assert.Equal("OneDrive", burst.Name);
            Assert.Equal(3, burst.At);
            Assert.Equal(60 * MB, burst.Bytes);
        }

        [Fact]
        public void MatchStats_IgnoresAReadingRightAfterTheLastOne()
        {
            // Ajustes salvos no meio da partida: uma checagem 10 ms depois da anterior não pode virar "pico de 100%".
            var t0 = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
            var stats = new MatchStats(t0, 1);
            stats.Add(t0, new[] { Cpu(1, "chrome", 0) });
            stats.Add(t0.AddMilliseconds(10), new[] { Cpu(1, "chrome", 156250) }); // 15,6 ms de processador em 10 ms
            stats.Add(t0.AddSeconds(3), new[] { Cpu(1, "chrome", 156250 + 300000) }); // 30 ms em 3 s: 1%

            Assert.Empty(stats.Finish("cs2").Culprits);
        }

        [Fact]
        public void MatchRecorder_SendsNoPingWhenTheNetworkXrayIsOff()
        {
            // Ajustes → Privacidade → Medir a rede no Raio-X desligado: a partida é gravada sem nenhum ping.
            Assert.True(new AppConfig().MeasureNetwork);
            var recorder = new MatchRecorder { MeasureNetwork = false };
            recorder.Start("cs2");
            Assert.True(recorder.Recording);
            Assert.False(recorder.MeasuringNetwork);
            Assert.Null(recorder.Stop()); // curta demais para virar relatório
        }

        private static ProcessSample Cpu(int pid, string name, long ticks)
        {
            return new ProcessSample { Pid = pid, Name = name, StartTime = 1, CpuTicks = ticks, IoBytes = 0 };
        }

        private static ProcessSample Sample(int pid, string name, long ioBytes)
        {
            return new ProcessSample { Pid = pid, Name = name, StartTime = 1, CpuTicks = 0, IoBytes = ioBytes };
        }
    }
}
