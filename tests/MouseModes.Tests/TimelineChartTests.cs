using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// O gráfico "A partida no tempo" do Raio-X: escalas, eixo de tempo, ponto sob o mouse e o cartão na janela, com
    /// partidas inventadas. Nada aqui grava o histórico de verdade nem abre o app.
    /// </summary>
    public class TimelineChartTests
    {
        /// <summary>Uma partida de minutes em baldes de 10 s: FPS 140 (com uma pausa), ping 22 e uma perda.</summary>
        private static MatchTimeline Timeline(double minutes, bool fps = true, bool ping = true)
        {
            var t = new MatchTimeline { BucketSeconds = 10 };
            for (double at = 0; at < minutes * 60; at += 10)
            {
                var p = new TimelinePoint { At = at };
                if (fps && at >= 10 && !(at >= 300 && at < 360))
                {
                    p.Fps = 140 + (float)(at % 70) / 10;
                    p.Low1Fps = 95;
                }
                if (ping) p.Ping = 22;
                if (ping && at == 600) p.Lost = 1;
                t.Points.Add(p);
            }
            t.Stutters.AddRange(new[] { 120.5, 610.0 });
            t.Slowdowns.Add(new TimelineSpan { At = 400, Seconds = 30 });
            return t;
        }

        private static MatchReport Report(MatchTimeline timeline, double minutes = 20)
        {
            return new MatchReport
            {
                Game = "cs2",
                Duration = TimeSpan.FromMinutes(minutes),
                Culprits = new List<Culprit>(),
                Frames = new FrameReport { Frames = 160000, AverageFps = 141, Low1Fps = 95, Low01Fps = 70, MedianMs = 7, StutterCount = 2, Worst = new List<Stutter>() },
                Timeline = timeline
            };
        }

        /// <summary>Abre o protegido OnMouseMove/OnMouseLeave para simular o mouse sem mostrar nada.</summary>
        private class ProbeChart : TimelineChart
        {
            public void MouseAt(int x)
            {
                OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, x, 10, 0));
            }

            public void MouseOut()
            {
                OnMouseLeave(EventArgs.Empty);
            }

            /// <summary>Pinta uma vez num bitmap (como a janela faria), para o mouse usar a geometria do desenho.</summary>
            public void PaintOnce()
            {
                using (var bmp = new Bitmap(Width, Height))
                using (Graphics g = Graphics.FromImage(bmp))
                    OnPaint(new PaintEventArgs(g, ClientRectangle));
            }
        }

        private static List<T> Find<T>(Control root) where T : Control
        {
            var found = new List<T>();
            foreach (Control c in root.Controls)
            {
                if (c is T) found.Add((T)c);
                found.AddRange(Find<T>(c));
            }
            return found;
        }

        [Theory]
        [InlineData(144, 3, 50, 150)]
        [InlineData(160, 3, 60, 180)]
        [InlineData(240, 3, 100, 300)]
        [InlineData(59.9, 3, 25, 75)]  // jogo travado em 60 FPS: a linha fica perto do alto, não a 2/3
        [InlineData(400, 3, 150, 450)] // e não 600: um terço da faixa vazio
        [InlineData(0, 3, 10, 30)]   // sem quadro nenhum: o piso
        [InlineData(25, 1, 30, 30)]  // ping baixo: só o topo
        [InlineData(88, 1, 100, 100)] // um pico de ping leva o topo junto
        public void FpsAndPingScalesUseRoundStepsAboveTheHighestValue(double max, int steps, double expectedStep, double expectedTop)
        {
            double step;
            double top = TimelineChart.NiceTop(max, steps, steps == 1 ? 10 : 30, out step);
            Assert.Equal(expectedStep, step, 6);
            Assert.Equal(expectedTop, top, 6);
        }

        [Fact]
        public void ScaleAlwaysFitsTheValueInAtMostTheGivenSteps()
        {
            for (double max = 1; max < 1000; max += 3.7)
                for (int steps = 1; steps <= 3; steps++)
                {
                    double step;
                    double top = TimelineChart.NiceTop(max, steps, 0, out step);
                    Assert.True(top >= max, max + " em " + steps);
                    Assert.True(Math.Round(top / step) <= steps, max + " em " + steps + ": " + top + "/" + step);
                }
        }

        [Theory]
        [InlineData(40, 8, 10)]
        [InlineData(180, 8, 30)]
        [InlineData(2, 8, 1)]
        [InlineData(65, 8, 10)]
        [InlineData(40, 3, 15)]  // 0, 15, 30: o último rótulo não precisa cair no fim
        [InlineData(600, 8, 120)]
        public void TimeAxisStepFitsTheLabels(double minutes, int labels, int expected)
        {
            int step = TimelineChart.TimeStepMinutes(minutes * 60, labels);
            Assert.Equal(expected, step);
            Assert.True(Math.Floor(minutes / step) <= labels - 1);
        }

        [Fact]
        public void SpanCoversTheDurationOrTheLastBucket()
        {
            MatchTimeline t = Timeline(20);
            Assert.Equal(1500, TimelineChart.Span(t, 1500));
            Assert.Equal(1200, TimelineChart.Span(t, 1000)); // o último balde passa da duração gravada
            Assert.Equal(600, TimelineChart.Span(null, 600));
        }

        [Fact]
        public void MouseFindsTheBucketUnderIt()
        {
            MatchTimeline t = Timeline(20); // 120 baldes de 10 s, 1200 s
            Assert.Equal(0, TimelineChart.IndexAt(t, 1200, 40, 360, 40));
            Assert.Equal(62, TimelineChart.IndexAt(t, 1200, 40, 360, 40 + 360 * 625 / 1200f));
            Assert.Equal(119, TimelineChart.IndexAt(t, 1200, 40, 360, 400));
            Assert.Equal(-1, TimelineChart.IndexAt(t, 1200, 40, 360, 39));
            Assert.Equal(-1, TimelineChart.IndexAt(t, 1200, 40, 360, 401));
            Assert.Equal(-1, TimelineChart.IndexAt(null, 1200, 40, 360, 100));

            // Baldes com buraco no meio: fica o mais perto.
            var sparse = new MatchTimeline { BucketSeconds = 10 };
            sparse.Points.Add(new TimelinePoint { At = 0, Ping = 20 });
            sparse.Points.Add(new TimelinePoint { At = 100, Ping = 20 });
            Assert.Equal(1, TimelineChart.IndexAt(sparse, 110, 0, 110, 80));
            Assert.Equal(0, TimelineChart.IndexAt(sparse, 110, 0, 110, 30));
        }

        [Fact]
        public void HoverShowsTheMinuteAndOnlyWhatWasMeasured()
        {
            var both = new TimelinePoint { At = 720, Fps = 143.4f, Low1Fps = 90, Ping = 21 };
            Assert.Equal("12 min · 143 FPS · 21 ms", TimelineChart.HoverText(both, 10));
            var pingOnly = new TimelinePoint { At = 725, Ping = 21 };
            Assert.Equal("12 min · 21 ms", TimelineChart.HoverText(pingOnly, 10));
            var paused = new TimelinePoint { At = 60, Lost = 2 };
            Assert.Equal("1 min · " + L.T("2 pings perdidos", "2 lost pings"), TimelineChart.HoverText(paused, 10));
        }

        [Fact]
        public void HoverFollowsTheMouseAndGoesAwayWhenItLeaves()
        {
            MatchTimeline timeline = Timeline(20);
            using (var chart = new ProbeChart { Size = new Size(408, TimelineChart.PreferredHeight) })
            {
                chart.SetData(timeline, 1200);
                chart.PaintOnce();
                // A área do gráfico vem do desenho (coluna medida com "150 FPS"), não do recuo provisório.
                Assert.True(chart.PlotW > 0);
                Assert.NotEqual(TimelineChart.Gutter, chart.PlotX);
                Assert.True(chart.PlotX + chart.PlotW <= 408);

                Assert.Equal(-1, chart.HoverIndex);
                chart.MouseAt(400);
                Assert.True(chart.HoverIndex > 100);
                chart.MouseAt((int)Math.Floor(chart.PlotX) - 1); // na coluna dos valores, fora do gráfico
                Assert.Equal(-1, chart.HoverIndex);
                chart.MouseAt((int)Math.Ceiling(chart.PlotX) + 1);
                Assert.Equal(0, chart.HoverIndex);
                chart.MouseAt(200);
                Assert.Equal(TimelineChart.IndexAt(timeline, 1200, chart.PlotX, chart.PlotW, 200), chart.HoverIndex);
                Assert.True(chart.HoverIndex >= 0);
                chart.MouseOut();
                Assert.Equal(-1, chart.HoverIndex);
            }
        }

        [Fact]
        public void GridLabelsPutTheFpsUnitOnlyOnTop()
        {
            Assert.Equal(new[] { "60", "120", "180 FPS" }, TimelineChart.GridLabels(180, 60, " FPS", true).ToArray());
            Assert.Equal(new[] { "100 ms" }, TimelineChart.GridLabels(100, 100, " ms", false).ToArray());
            Assert.Equal(new[] { "30 ms", "60 ms" }, TimelineChart.GridLabels(60, 30, " ms", false).ToArray());
        }

        [Fact]
        public void SlowdownLegendUsesTheSameColorAsTheChart()
        {
            LegendItem slow = MatchReportForm.LegendFor(Timeline(20)).Find(i => i.Square);
            Assert.NotNull(slow);
            Assert.Equal(Theme.Alpha(Theme.Warning, TimelineChart.SlowdownAlpha), slow.Color);
            Assert.True(slow.Outline.A > 0);
        }

        [Fact]
        public void ScreenReaderGetsTheChartInWords()
        {
            using (var chart = new TimelineChart())
            {
                chart.SetData(Timeline(20), 1200);
                string text = chart.AccessibleDescription;
                Assert.Contains("20 min", text);
                Assert.Contains("FPS", text);
                Assert.Contains(L.T("2 travadinhas", "2 stutters"), text);
                Assert.Contains("22 ms", text);
                Assert.Equal(AccessibleRole.Chart, chart.AccessibleRole);
                Assert.False(string.IsNullOrEmpty(chart.AccessibleName));
            }
        }

        [Fact]
        public void LegendWrapsWholeItemsOnlyWhenTheyDoNotFit()
        {
            Assert.Equal(new[] { 0, 0, 0 }, ChartLegend.RowsFor(new[] { 80f, 80f, 80f }, 408));
            Assert.Equal(new[] { 0, 0, 1 }, ChartLegend.RowsFor(new[] { 150f, 150f, 150f }, 408));
            Assert.Equal(new[] { 0, 1 }, ChartLegend.RowsFor(new[] { 500f, 20f }, 408)); // um item maior que a linha fica sozinho
        }

        [Fact]
        public void ReportWindowShowsTheTimelineCardOnlyWhenTheMatchHasOne()
        {
            string header = L.T("A partida no tempo", "The match over time");
            using (var form = new MatchReportForm(Report(Timeline(20)), new AppConfig(), null))
            {
                TimelineChart chart = Assert.Single(Find<TimelineChart>(form));
                Assert.IsType<Card>(chart.Parent);
                Assert.True(chart.Bottom <= chart.Parent.Height && chart.Right <= chart.Parent.Width);
                ChartLegend legend = Assert.Single(Find<ChartLegend>(form));
                Assert.Same(chart.Parent, legend.Parent);
                Assert.True(legend.Bottom <= legend.Parent.Height);
                Assert.Equal(5, legend.Items.Count); // FPS, travadinha, freio, ping, perda
                Assert.Contains(header, Texts(form));
            }

            // Partida antiga (sem a série) e série sem nada medido: sem cartão.
            foreach (MatchTimeline none in new[] { null, new MatchTimeline { BucketSeconds = 5 } })
                using (var form = new MatchReportForm(Report(none), new AppConfig(), null))
                {
                    Assert.Empty(Find<TimelineChart>(form));
                    Assert.Empty(Find<ChartLegend>(form));
                    Assert.DoesNotContain(header, Texts(form));
                }
        }

        [Fact]
        public void LegendOnlyNamesWhatTheMatchHas()
        {
            MatchTimeline pingOnly = Timeline(20, fps: false);
            pingOnly.Stutters.Clear();
            pingOnly.Slowdowns.Clear();
            List<LegendItem> items = MatchReportForm.LegendFor(pingOnly);
            Assert.Equal(new[] { "Ping", L.T("Perda", "Lost ping") }, items.ConvertAll(i => i.Text).ToArray());

            MatchTimeline fpsOnly = Timeline(20, ping: false);
            items = MatchReportForm.LegendFor(fpsOnly);
            Assert.Equal(new[] { L.T("FPS e 1% low", "FPS and 1% low"), L.T("Travadinha", "Stutter"), L.T("Processador freado", "CPU throttled") },
                         items.ConvertAll(i => i.Text).ToArray());
        }

        [Fact]
        public void NoTextInTheReportIsCutWithTheTimeline()
        {
            // Só no idioma em que os testes rodam: L.Init muda o estado global (o inglês é conferido nos renders).
            using (var form = new MatchReportForm(Report(Timeline(40), 40), new AppConfig(), null))
            {
                foreach (Label c in Find<Label>(form))
                {
                    if (string.IsNullOrEmpty(c.Text)) continue;
                    Size needed = TextRenderer.MeasureText(c.Text, c.Font, new Size(c.Width, int.MaxValue), TextFormatFlags.WordBreak);
                    Assert.True(needed.Height <= c.Height + 2, "cortado: " + c.Text);
                }
                // A legenda cabe na largura (quebrando item inteiro, se precisar) e na altura que recebeu.
                ChartLegend legend = Assert.Single(Find<ChartLegend>(form));
                float[] widths = ChartLegend.ItemWidths(legend.Items);
                foreach (float w in widths) Assert.True(w <= legend.Width, "legenda larga demais");
                int[] rows = ChartLegend.RowsFor(widths, legend.Width);
                Assert.True((rows[rows.Length - 1] + 1) * ChartLegend.LineHeight <= legend.Height);
            }
        }

        private static string Texts(Control root)
        {
            var text = new System.Text.StringBuilder();
            foreach (Control c in root.Controls)
            {
                text.Append(c.Text).Append('\n');
                text.Append(Texts(c));
            }
            return text.ToString();
        }
    }
}
