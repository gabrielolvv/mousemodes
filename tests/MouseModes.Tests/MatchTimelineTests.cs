using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>A partida no tempo (gráfico do Raio-X) com quadros, pings e freios inventados: nada aqui lê o Windows.</summary>
    public class MatchTimelineTests
    {
        /// <summary>Quadros de ms cada, de from até from + seconds.</summary>
        private static List<FrameSample> Steady(double from, double seconds, float ms)
        {
            var list = new List<FrameSample>();
            for (double t = from; t < from + seconds; t += ms / 1000.0) list.Add(new FrameSample(t, ms));
            return list;
        }

        /// <summary>Um ping por segundo, de from até to (exclusive), todos com o mesmo valor.</summary>
        private static List<NetSample> Pings(int from, int to, int ms)
        {
            var list = new List<NetSample>();
            for (int t = from; t < to; t++) list.Add(new NetSample(t, 2, ms));
            return list;
        }

        [Fact]
        public void Build_IsNullWithoutFpsOrPing()
        {
            Assert.Null(MatchTimeline.Build(null, null, null, null, 600));
            Assert.Null(MatchTimeline.Build(null, null, null, null, 0));
            // Só o aquecimento, nenhum ping medido, tudo perdido: nada para desenhar.
            Assert.Null(MatchTimeline.Build(Steady(0, 9, 10f), null, null, null, 600));
            var notMeasured = new List<NetSample> { new NetSample(5, 2, NetSample.NotMeasured) };
            Assert.Null(MatchTimeline.Build(null, notMeasured, null, null, 600));
            var allLost = new List<NetSample> { new NetSample(5, 2, NetSample.Lost), new NetSample(6, 2, NetSample.Lost) };
            Assert.Null(MatchTimeline.Build(null, allLost, null, null, 600));
        }

        [Fact]
        public void Build_GivesFpsAndOnePercentLowPerBucketAfterTheWarmup()
        {
            List<FrameSample> frames = Steady(0, 120, 10f); // 100 FPS cravados
            frames.Add(new FrameSample(62.0, 50f));         // um quadro lento entre 60 e 65 s
            frames.Sort((a, b) => a.At.CompareTo(b.At));

            MatchTimeline t = MatchTimeline.Build(frames, null, null, null, 120);

            Assert.Equal(5, t.BucketSeconds);
            Assert.Equal(24, t.Points.Count);
            Assert.Equal(0, t.Points[0].At);
            Assert.Equal(115, t.Points[23].At);
            Assert.False(t.Points[0].HasFps); // aquecimento
            Assert.False(t.Points[1].HasFps);
            Assert.True(float.IsNaN(t.Points[1].Low1Fps));
            Assert.Equal(100, t.Points[2].Fps, 0);
            Assert.Equal(100, t.Points[2].Low1Fps, 0);
            Assert.True(t.Points[12].Low1Fps < 60);         // o 1% low do balde sente o quadro lento
            Assert.True(t.Points[12].Fps > 95);             // a média quase não
            Assert.All(t.Points, p => Assert.False(p.HasPing));
            Assert.All(t.Points, p => Assert.Equal(0, p.Lost));
            Assert.Empty(t.Stutters);
            Assert.Empty(t.Slowdowns);
        }

        [Fact]
        public void Build_LeavesAGapWhereTheGamePaused()
        {
            List<FrameSample> frames = Steady(10, 50, 10f);  // joga de 10 a 60 s
            frames.Add(new FrameSample(90, 30000f));         // alt-tab de 30 s: quadro de pausa, fica de fora
            frames.AddRange(Steady(90.01, 30, 10f));         // volta aos 90 s

            MatchTimeline t = MatchTimeline.Build(frames, null, null, null, 120);

            Assert.True(t.Points[11].HasFps);
            for (int i = 12; i < 18; i++) Assert.False(t.Points[i].HasFps); // 60 a 90 s: lacuna, nunca 0
            Assert.True(t.Points[18].HasFps);
            Assert.Equal(100, t.Points[18].Fps, 0);
        }

        [Fact]
        public void Build_PingIsTheMedianOfTheAnswersAndCountsTheLosses()
        {
            List<NetSample> net = Pings(0, 120, 20);
            net[30] = new NetSample(30, 2, 200);              // pico
            net[31] = new NetSample(31, 2, NetSample.Lost);
            net[32] = new NetSample(32, 2, NetSample.Lost);
            for (int s = 40; s < 45; s++) net[s] = new NetSample(s, 2, NetSample.Lost); // um balde inteiro perdido
            net[50] = new NetSample(50, NetSample.NotMeasured, NetSample.NotMeasured);  // sem medida: não conta

            MatchTimeline t = MatchTimeline.Build(null, net, null, null, 120);

            Assert.Equal(20, t.Points[0].Ping);
            Assert.Equal(20, t.Points[6].Ping); // 200, 20, 20: a mediana não se deixa levar por um pico
            Assert.Equal(2, t.Points[6].Lost);
            Assert.Equal(TimelinePoint.NoPing, t.Points[8].Ping);
            Assert.False(t.Points[8].HasPing);
            Assert.Equal(5, t.Points[8].Lost);
            Assert.Equal(20, t.Points[10].Ping);
            Assert.Equal(0, t.Points[10].Lost);
            Assert.All(t.Points, p => Assert.False(p.HasFps)); // sem quadros (sem permissão)
        }

        [Fact]
        public void Build_WithPingBlockedKeepsTheFpsButNoLosses()
        {
            // 10 min a 100 FPS e nenhum ping respondendo (ICMP bloqueado): a rede não foi medida, então não há "perda".
            List<FrameSample> frames = Steady(0, 600, 10f);
            var net = new List<NetSample>();
            for (int s = 0; s < 600; s += 2) net.Add(new NetSample(s, 2, NetSample.Lost));

            MatchTimeline t = MatchTimeline.Build(frames, net, null, null, 600);

            Assert.Contains(t.Points, p => p.HasFps);
            Assert.All(t.Points, p => Assert.False(p.HasPing));
            Assert.All(t.Points, p => Assert.Equal(0, p.Lost));
            Assert.DoesNotContain(L.T("Perda", "Lost ping"), MatchReportForm.LegendFor(t).ConvertAll(i => i.Text));
            Assert.DoesNotContain(L.T("perdido", "lost"), TimelineChart.HoverText(t.Points[10], t.BucketSeconds));
        }

        [Fact]
        public void LowFps_WithoutSortingMatchesTheSortedAnswer()
        {
            var random = new Random(7);
            foreach (int n in new[] { 1, 2, 49, 50, 51, 149, 150, 1000, 12345 })
            {
                var ms = new float[n + 10];
                for (int i = 0; i < ms.Length; i++) ms[i] = (float)(3 + random.NextDouble() * 20 + (random.Next(200) == 0 ? 80 : 0));
                var segment = new List<float>();
                for (int i = 5; i < 5 + n; i++) segment.Add(ms[i]);
                segment.Sort();
                Assert.Equal(FrameStats.LowFps(segment, 0.01), MatchTimeline.LowFps(ms, 5, n, 0.01), 4);
            }
        }

        [Fact]
        public void Build_EndsAtTheLastMeasureWithoutBucketsAfterIt()
        {
            // O ping roda até o Stop, uns segundos depois do último tick (que dá a duração).
            List<NetSample> net = Pings(0, 123, 20);
            net.Add(new NetSample(123.4, 2, 30));
            MatchTimeline late = MatchTimeline.Build(null, net, null, null, 120);
            Assert.Equal(25, late.Points.Count);
            Assert.Equal(120, late.Points[24].At);
            Assert.Equal(20, late.Points[24].Ping);

            // Medida que acaba antes da duração: os baldes vão até a duração, sem passar dela.
            MatchTimeline early = MatchTimeline.Build(null, Pings(0, 60, 20), null, null, 120);
            Assert.Equal(24, early.Points.Count);
            Assert.False(early.Points[23].HasPing);
        }

        [Fact]
        public void BucketGrowsInStepsOfFiveSecondsToFitTheChart()
        {
            Assert.Equal(5, MatchTimeline.BucketFor(120));
            Assert.Equal(5, MatchTimeline.BucketFor(1500));    // 25 min: 300 baldes de 5 s
            Assert.Equal(10, MatchTimeline.BucketFor(1500.5));
            Assert.Equal(40, MatchTimeline.BucketFor(3 * 3600)); // 3 h

            MatchTimeline t = MatchTimeline.Build(null, Pings(0, 3 * 3600, 20), null, null, 3 * 3600);
            Assert.Equal(40, t.BucketSeconds);
            Assert.Equal(270, t.Points.Count);
            Assert.True(t.Points.Count <= MatchTimeline.MaxPoints);
            Assert.Equal(3 * 3600 - 40, t.Points[t.Points.Count - 1].At);
        }

        [Fact]
        public void Build_KeepsTheStrongestStuttersInTimeOrder()
        {
            var stutters = new List<Stutter>();
            for (int i = 0; i < 500; i++) stutters.Add(new Stutter { At = 10 + i * 10.04, Ms = 40 + (i * 37) % 500 }); // força embaralhada

            MatchTimeline t = MatchTimeline.Build(null, Pings(0, 5100, 20), stutters, null, 5100);

            Assert.Equal(MatchTimeline.MaxStutters, t.Stutters.Count);
            var expected = new List<double>();
            foreach (Stutter s in stutters)
                if (s.Ms >= 40 + 200) expected.Add(Math.Round(s.At, 1)); // as 300 mais fortes
            Assert.Equal(expected, t.Stutters);
            for (int i = 1; i < t.Stutters.Count; i++) Assert.True(t.Stutters[i] > t.Stutters[i - 1]);
        }

        [Fact]
        public void Build_CopiesTheSlowdownsInTimeOrder()
        {
            var slowdowns = new List<CpuSlowdown>
            {
                new CpuSlowdown { At = 900, Seconds = 30, LowestLimit = 80 },
                new CpuSlowdown { At = 300.04, Seconds = 90, LowestLimit = 70 },
                new CpuSlowdown { At = 1190, Seconds = 60, LowestLimit = 70 } // passa do fim: corta no fim
            };

            MatchTimeline t = MatchTimeline.Build(null, Pings(0, 1200, 20), null, slowdowns, 1200);

            Assert.Equal(3, t.Slowdowns.Count);
            Assert.Equal(300, t.Slowdowns[0].At);
            Assert.Equal(90, t.Slowdowns[0].Seconds);
            Assert.Equal(900, t.Slowdowns[1].At);
            Assert.Equal(10, t.Slowdowns[2].Seconds);
        }

        [Fact]
        public void Analyze_KeepsEveryStutterForTheTimeline()
        {
            List<FrameSample> frames = Steady(10, 60, 7f);
            for (int i = 0; i < 5; i++) frames.Add(new FrameSample(15 + i * 10, 85f));
            frames.Sort((a, b) => a.At.CompareTo(b.At));

            FrameReport report = FrameStats.Analyze(frames, null, null, null);

            Assert.Equal(5, report.StutterCount);
            Assert.Equal(FrameStats.MaxWorst, report.Worst.Count);
            Assert.Equal(new[] { 15.0, 25.0, 35.0, 45.0, 55.0 }, report.All.ConvertAll(s => s.At).ToArray());
            Assert.Equal(5, MatchTimeline.Build(frames, null, report.All, null, 70).Stutters.Count);
        }

        private static MatchTimeline Sample()
        {
            var t = new MatchTimeline { BucketSeconds = 5 };
            t.Points.Add(new TimelinePoint { At = 0, Ping = 21 });                                        // aquecimento: sem FPS
            t.Points.Add(new TimelinePoint { At = 5, Fps = 143.6f, Low1Fps = 92.3f, Ping = 22 });
            t.Points.Add(new TimelinePoint { At = 10, Fps = 141.2f, Low1Fps = 88.8f, Lost = 5 });         // tudo perdido
            t.Stutters.Add(7.5);
            t.Slowdowns.Add(new TimelineSpan { At = 3, Seconds = 9 });
            return t;
        }

        private static MatchRecord RoundTrip(MatchRecord record, out string xml)
        {
            var serializer = new XmlSerializer(typeof(MatchRecord));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, record);
                xml = writer.ToString();
            }
            return (MatchRecord)serializer.Deserialize(new StringReader(xml));
        }

        [Fact]
        public void TimelineGoesThroughTheHistoryXmlWithGapsAndMissingPings()
        {
            var report = new MatchReport { Game = "cs2", Duration = TimeSpan.FromMinutes(30), Timeline = Sample() };
            MatchRecord record = MatchRecord.From(report, null, new DateTime(2026, 11, 16, 21, 40, 0, DateTimeKind.Utc));
            Assert.Same(report.Timeline, record.Timeline);

            string xml;
            MatchTimeline back = RoundTrip(record, out xml).ToReport().Timeline;

            Assert.Contains("f=\"NaN\"", xml);
            Assert.Contains("p=\"-1\"", xml);
            Assert.Equal(5, back.BucketSeconds);
            Assert.Equal(3, back.Points.Count);
            Assert.False(back.Points[0].HasFps);
            Assert.True(float.IsNaN(back.Points[0].Low1Fps));
            Assert.Equal(21, back.Points[0].Ping);
            Assert.Equal(143.6f, back.Points[1].Fps);
            Assert.Equal(92.3f, back.Points[1].Low1Fps);
            Assert.Equal(TimelinePoint.NoPing, back.Points[2].Ping);
            Assert.Equal(5, back.Points[2].Lost);
            Assert.Equal(7.5, Assert.Single(back.Stutters));
            TimelineSpan span = Assert.Single(back.Slowdowns);
            Assert.Equal(3, span.At);
            Assert.Equal(9, span.Seconds);
            Assert.True(back.HasData);
        }

        [Fact]
        public void MatchWithoutTimelineStaysWithoutOneAndEmptyListsComeBackEmpty()
        {
            var none = new MatchRecord { Game = "cs2", DurationSeconds = 600 };
            Assert.Null(none.ToReport().Timeline);

            var hollow = new MatchRecord { Game = "cs2", DurationSeconds = 600, Timeline = new MatchTimeline { BucketSeconds = 5 } };
            hollow.Timeline.Points = null;
            hollow.Timeline.Stutters = null;
            hollow.Timeline.Slowdowns = null;
            MatchTimeline fixedUp = hollow.ToReport().Timeline;
            Assert.NotNull(fixedUp.Points);
            Assert.Empty(fixedUp.Stutters);
            Assert.Empty(fixedUp.Slowdowns);
            Assert.False(fixedUp.HasData);

            string xml;
            MatchRecord back = RoundTrip(new MatchRecord { Game = "cs2", DurationSeconds = 600 }, out xml);
            Assert.DoesNotContain("<Timeline", xml);
            Assert.Null(back.ToReport().Timeline);
        }
    }
}
