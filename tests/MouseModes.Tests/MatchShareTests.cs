using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace MouseModes.Tests
{
    /// <summary>
    /// Compartilhar a partida: o texto, a imagem e o pacote da área de transferência, com partidas inventadas. Nada aqui
    /// toca a área de transferência de verdade (o destino é falso) nem abre o app.
    /// </summary>
    public class MatchShareTests
    {
        /// <summary>Área de transferência falsa: guarda o que recebeu e responde o que o teste mandar.</summary>
        private class FakeClipboard : IClipboardTarget
        {
            public DataObject Received;
            public bool Accept = true;
            public int Calls;

            public bool Set(DataObject data)
            {
                Calls++;
                Received = data;
                return Accept;
            }
        }

        /// <summary>Uma partida de 40 min em baldes de 10 s: FPS ~146, ping 22, uma perda, travadinhas e um freio.</summary>
        private static MatchTimeline Timeline()
        {
            var t = new MatchTimeline { BucketSeconds = 10 };
            for (double at = 0; at < 2400; at += 10)
            {
                var p = new TimelinePoint { At = at, Ping = 22 };
                if (at >= 10)
                {
                    p.Fps = 140 + (float)(at % 90) / 10;
                    p.Low1Fps = 91;
                }
                if (at == 1500) p.Lost = 1;
                t.Points.Add(p);
            }
            t.Stutters.AddRange(new[] { 612.4, 1801.2 });
            t.Slowdowns.Add(new TimelineSpan { At = 1785, Seconds = 66 });
            return t;
        }

        /// <summary>Partida completa e inventada, com nomes de programas que nunca podem sair no compartilhar.</summary>
        private static MatchReport Full()
        {
            return new MatchReport
            {
                Game = "cs2",
                Duration = TimeSpan.FromMinutes(40),
                Culprits = new List<Culprit> { new Culprit { Name = "programa-secreto", AveragePercent = 4, PeakPercent = 30 } },
                Frames = new FrameReport
                {
                    Frames = 350000, AverageFps = 146.2, Low1Fps = 91.4, Low01Fps = 60, MedianMs = 6.8, StutterCount = 6,
                    Worst = new List<Stutter> { new Stutter { At = 610, Ms = 85, Cause = "nessa hora o outro-segredo usava 34% do processador" } }
                },
                Network = new NetworkReport
                {
                    Samples = 2400, InternetMedian = 22, Jitter = 3.2, LossPercent = 0.4, GatewayMedian = 2, Verdict = NetVerdict.Stable, SpikeCount = 1,
                    Spikes = new List<NetSpike> { new NetSpike { At = 1500, Seconds = 4, PeakMs = 88, Busy = "baixador-secreto", BusyBytes = 1 } }
                },
                CpuSpeed = new CpuSpeedReport { AveragePerformance = 97, MeasuredSeconds = 2400, Worst = new List<CpuSlowdown>() },
                Timeline = Timeline()
            };
        }

        private static MatchRecord Record(MatchReport report)
        {
            return MatchRecord.From(report, null, new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
        }

        private static string[] Lines(string text)
        {
            return text.Split(new[] { "\r\n" }, StringSplitOptions.None);
        }

        [Fact]
        public void TextHasOneShortLinePerThingMeasured()
        {
            MatchReport report = Full();
            MatchRecord record = Record(report);
            string[] lines = Lines(MatchShare.Text(report, record));

            Assert.Equal(new[]
            {
                "cs2 · 40 min · " + record.When(),
                L.T("146 FPS de média · 1% low 91 FPS · 6 travadinhas", "146 FPS average · 1% low 91 FPS · 6 stutters"),
                L.T("Ping 22 ms · jitter 3 ms · perda 0,4% · rede estável, com picos curtos", "Ping 22 ms · jitter 3 ms · loss 0.4% · network stable, short spikes"),
                L.T("Processador: 97% da velocidade nominal · sem freio", "CPU: 97% of nominal speed · no throttling"),
                L.T("Raio-X do MouseModes", "MouseModes match X-ray")
            }, lines);
            // Os números são os mesmos textos do Raio-X.
            Assert.Equal(report.Frames.Summary(), lines[1]);
        }

        [Fact]
        public void CpuLineWithThrottlingKeepsTheRaioXNumbersWithoutOnAverage()
        {
            var cpu = new CpuSpeedReport { AveragePerformance = 96.6, MeasuredSeconds = 2400, LimitedSeconds = 125, LowestLimit = 70.2f, Worst = new List<CpuSlowdown>() };

            Assert.Equal(L.T("Processador: 97% da velocidade nominal · freado 2 min (até 70%)", "CPU: 97% of nominal speed · throttled 2 min (down to 70%)"),
                MatchShare.CpuLine(cpu));
            Assert.DoesNotContain(L.T("em média", "on average"), MatchShare.CpuLine(cpu));
        }

        [Fact]
        public void TextNeverNamesBackgroundPrograms()
        {
            MatchReport report = Full();
            string text = MatchShare.Text(report, Record(report));
            Assert.DoesNotContain("secreto", text);
            Assert.DoesNotContain("segredo", text);
            Assert.DoesNotContain("\\", text);
        }

        [Fact]
        public void TextWithoutNetworkSkipsThePingLine()
        {
            MatchReport report = Full();
            report.Network = null;
            string[] lines = Lines(MatchShare.Text(report, null));
            Assert.Equal(4, lines.Length);
            Assert.Equal("cs2 · 40 min", lines[0]);
            Assert.DoesNotContain(lines, l => l.StartsWith("Ping", StringComparison.Ordinal));
        }

        [Fact]
        public void TextWithoutFramesSkipsTheFpsLine()
        {
            MatchReport report = Full();
            report.Frames = null;
            report.FramesMissing = "sem permissão";
            string text = MatchShare.Text(report, null);
            Assert.DoesNotContain("FPS", text);
            Assert.DoesNotContain("permissão", text);
            Assert.Equal(4, Lines(text).Length);
        }

        [Fact]
        public void TextWithOnlyThePingHasThreeLines()
        {
            var report = new MatchReport
            {
                Game = "r5apex",
                Duration = TimeSpan.FromMinutes(65),
                Culprits = new List<Culprit>(),
                Network = new NetworkReport { Samples = 3900, InternetMedian = 40, Jitter = 1, LossPercent = 0, GatewayMedian = -1, Wifi = true, Verdict = NetVerdict.Local, Spikes = new List<NetSpike>() }
            };
            Assert.Equal(new[]
            {
                "r5apex · 1 h 05 min",
                L.T("Ping 40 ms · jitter 1 ms · perda 0% · problemas no Wi-Fi", "Ping 40 ms · jitter 1 ms · loss 0% · Wi-Fi trouble"),
                L.T("Raio-X do MouseModes", "MouseModes match X-ray")
            }, Lines(MatchShare.Text(report, null)));
        }

        [Theory]
        [InlineData(2f)]
        [InlineData(1f)]
        public void ImageHasTheShareWidthAndTheContentHeight(float scale)
        {
            MatchReport report = Full();
            using (Bitmap image = MatchShare.Image(report, Record(report), scale))
            {
                Assert.Equal((int)Math.Round(MatchShare.ImageWidth * scale), image.Width);
                Assert.True(image.Height > 300 * scale, "baixa demais: " + image.Height);
                Assert.Equal(96f * scale, image.HorizontalResolution, 1);
                // A margem é o fundo da janela; o meio do cartão é a superfície.
                Assert.Equal(Theme.Bg.ToArgb(), image.GetPixel(1, 1).ToArgb());
                Assert.Equal(Theme.Bg.ToArgb(), image.GetPixel(image.Width - 2, image.Height - 2).ToArgb());
            }
        }

        [Fact]
        public void ImageWorksWithoutTimelineAndWithNothingButTheGame()
        {
            MatchReport full = Full();
            MatchReport noTimeline = Full();
            noTimeline.Timeline = null;
            var bare = new MatchReport { Game = "cs2", Duration = TimeSpan.FromMinutes(12) };
            var emptySeries = Full();
            emptySeries.Timeline = new MatchTimeline { BucketSeconds = 5 };

            int fullHeight, noTimelineHeight, bareHeight;
            using (Bitmap image = MatchShare.Image(full, Record(full))) fullHeight = image.Height;
            using (Bitmap image = MatchShare.Image(noTimeline, null)) noTimelineHeight = image.Height;
            using (Bitmap image = MatchShare.Image(bare, null))
            {
                bareHeight = image.Height;
                Assert.Equal(MatchShare.ImageWidth * 2, image.Width);
            }
            using (Bitmap image = MatchShare.Image(emptySeries, null)) Assert.Equal(noTimelineHeight, image.Height);

            Assert.True(bareHeight > 0);
            Assert.True(bareHeight < noTimelineHeight, bareHeight + " / " + noTimelineHeight);
            Assert.True(noTimelineHeight < fullHeight, noTimelineHeight + " / " + fullHeight);
        }

        [Fact]
        public void CopyHandsTextBitmapAndPngToTheClipboard()
        {
            MatchReport report = Full();
            MatchRecord record = Record(report);
            var clipboard = new FakeClipboard();

            Assert.True(MatchShare.Copy(report, record, clipboard));
            Assert.Equal(1, clipboard.Calls);
            DataObject data = clipboard.Received;
            Assert.NotNull(data);

            Assert.Equal(MatchShare.Text(report, record), data.GetData(DataFormats.UnicodeText));
            var bitmap = Assert.IsType<Bitmap>(data.GetData(DataFormats.Bitmap));
            Assert.Equal(MatchShare.ImageWidth * 2, bitmap.Width);

            var png = Assert.IsType<MemoryStream>(data.GetData(MatchShare.PngFormat));
            byte[] bytes = png.ToArray();
            Assert.True(bytes.Length > 8);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, new[] { bytes[0], bytes[1], bytes[2], bytes[3] });
            Assert.Equal(0, png.Position);
            bitmap.Dispose();
        }

        [Fact]
        public void CopyReportsWhenTheClipboardRefuses()
        {
            MatchReport report = Full();
            var busy = new FakeClipboard { Accept = false };
            Assert.False(MatchShare.Copy(report, null, busy));
            Assert.Equal(1, busy.Calls);
        }

        // ---------------------------------------------------------------- botão Copiar no Raio-X

        /// <summary>O clique de verdade do botão (o mesmo OnClick que o mouse, o Enter e o Espaço chamam).</summary>
        private static void Click(Control button)
        {
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
        }

        private static NeonButton CloseButton(MatchReportForm form)
        {
            NeonButton close = null;
            foreach (Control c in form.Controls)
                if (c is NeonButton && c.Text == L.T("Fechar", "Close")) close = (NeonButton)c;
            Assert.NotNull(close);
            return close;
        }

        [Fact]
        public void CopyButtonSitsLeftOfCloseWithoutOverlapping()
        {
            MatchReport report = Full();
            MatchRecord record = Record(report);
            // A partida que acabou (com o registro) e uma sem registro: o mesmo botão no mesmo lugar.
            foreach (MatchRecord r in new[] { record, null })
                using (var form = new MatchReportForm(report, new AppConfig(), null, r, null))
                {
                    NeonButton close = CloseButton(form);
                    NeonButton copy = form.CopyButton;
                    Assert.NotNull(copy);
                    Assert.Same(form, copy.Parent);
                    Assert.Equal(L.T("Copiar", "Copy"), copy.Text);
                    Assert.Equal(close.Top, copy.Top);
                    Assert.Equal(36, copy.Height);
                    Assert.Equal(close.Left - 12, copy.Right);
                    Assert.True(copy.Left > 20);
                    Assert.False(string.IsNullOrEmpty(copy.AccessibleDescription));

                    Label warning = form.CopyWarning;
                    Assert.Equal("", warning.Text);
                    Assert.True(warning.Right <= copy.Left, "aviso por cima do botão");
                    Assert.True(warning.Top >= close.Top && warning.Bottom <= close.Bottom, "aviso fora da linha dos botões");
                    Assert.True(form.ClientSize.Height >= close.Bottom);
                }
        }

        [Fact]
        public void ClickingCopySendsTheMatchAndSaysCopied()
        {
            MatchReport report = Full();
            MatchRecord record = Record(report);
            var clipboard = new FakeClipboard();
            using (var form = new MatchReportForm(report, new AppConfig(), null, record, null))
            {
                form.ShareTarget = clipboard;
                Click(form.CopyButton);

                Assert.Equal(1, clipboard.Calls);
                Assert.Equal(MatchShare.Text(report, record), clipboard.Received.GetData(DataFormats.UnicodeText));
                Assert.IsType<Bitmap>(clipboard.Received.GetData(DataFormats.Bitmap));
                Assert.Equal(L.T("Copiado", "Copied"), form.CopyButton.Text);
                Assert.Equal(L.T("Copiado", "Copied"), form.CopyButton.AccessibleName);
                Assert.Equal("", form.CopyWarning.Text);

                // Os 2 s passaram: o botão volta a dizer Copiar.
                form.RestoreCopyButton();
                Assert.Equal(L.T("Copiar", "Copy"), form.CopyButton.Text);
                ((Bitmap)clipboard.Received.GetData(DataFormats.Bitmap)).Dispose();
            }
        }

        [Fact]
        public void ClickingCopyWhenTheClipboardRefusesShowsAShortWarning()
        {
            MatchReport report = Full();
            var clipboard = new FakeClipboard { Accept = false };
            using (var form = new MatchReportForm(report, new AppConfig(), null, Record(report), null))
            {
                form.ShareTarget = clipboard;
                Click(form.CopyButton);

                Assert.Equal(1, clipboard.Calls);
                Assert.Equal(L.T("Copiar", "Copy"), form.CopyButton.Text);
                Assert.Equal(L.T("Não deu para copiar. Tente de novo.", "Couldn't copy. Try again."), form.CopyWarning.Text);

                // Tentou de novo e deu: o aviso some.
                clipboard.Accept = true;
                Click(form.CopyButton);
                Assert.Equal(2, clipboard.Calls);
                Assert.Equal("", form.CopyWarning.Text);
                Assert.Equal(L.T("Copiado", "Copied"), form.CopyButton.Text);
                ((Bitmap)clipboard.Received.GetData(DataFormats.Bitmap)).Dispose();
            }
        }

        [Fact]
        public void ChartDrawsInsideTheGivenRectangleOnly()
        {
            // O mesmo desenho do OnPaint, deslocado: nada sai do retângulo (a imagem de compartilhar põe o gráfico no meio).
            using (var bmp = new Bitmap(500, 260))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var chart = new TimelineChart())
            {
                g.Clear(Color.Magenta);
                chart.SetData(Timeline(), 2400);
                var r = new RectangleF(40, 50, 408, TimelineChart.PreferredHeight);
                chart.Draw(g, r, 1f, false);
                g.Flush();

                bool drewInside = false;
                for (int x = 0; x < bmp.Width; x++)
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        bool magenta = bmp.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb();
                        bool inside = x >= r.X - 1 && x <= r.Right + 1 && y >= r.Y - 1 && y <= r.Bottom + 1;
                        if (!inside) Assert.True(magenta, "fora do retângulo: " + x + "," + y);
                        else if (!magenta) drewInside = true;
                    }
                Assert.True(drewInside);
                Assert.True(chart.PlotX > r.X && chart.PlotX + chart.PlotW <= r.Right);
            }
        }
    }
}
