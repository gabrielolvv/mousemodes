using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// Compartilhar a partida (Discord, grupo do time): um texto curto e uma imagem de cartão com os números, o gráfico e
    /// a rede, no idioma da interface. Nunca leva nomes de programas em segundo plano nem caminhos, só o jogo e as medidas.
    /// </summary>
    public static class MatchShare
    {
        // Medidas a 96 dpi: largura da imagem, margem em volta do cartão, respiro dentro dele e o vão entre os blocos.
        internal const int ImageWidth = 520;
        private const int Margin = 16, Pad = 20, Gap = 14, ChipsHeight = 60, Dot = 8, LogoSize = 16;
        private const int Inner = ImageWidth - Margin * 2 - Pad * 2;

        /// <summary>Formato que alguns apps preferem para colar imagem (com o Bitmap ao lado para os outros).</summary>
        internal const string PngFormat = "PNG";

        private const string LineBreak = "\r\n";

        // ---------------------------------------------------------------- texto

        /// <summary>
        /// "cs2 · 40 min · 18/11 09:00", os quadros, a rede, o processador e "Raio-X do MouseModes", uma linha cada e só
        /// do que foi medido. Os números saem dos mesmos textos do Raio-X, para baterem com a tela.
        /// </summary>
        /// <param name="record">A partida no histórico, para a data (null = sem data).</param>
        public static string Text(MatchReport report, MatchRecord record)
        {
            var lines = new List<string> { Header(report, record, " · ") };
            if (report.Frames != null) lines.Add(report.Frames.Summary());
            if (report.Network != null) lines.Add(NetworkLine(report.Network));
            if (report.CpuSpeed != null) lines.Add(CpuLine(report.CpuSpeed));
            lines.Add(Signature());
            return string.Join(LineBreak, lines.ToArray());
        }

        /// <summary>"cs2 · 40 min · 18/11 09:00" (sem a data quando não há registro).</summary>
        private static string Header(MatchReport report, MatchRecord record, string separator)
        {
            string text = (string.IsNullOrEmpty(report.Game) ? "?" : report.Game) + separator + MatchReport.FormatDuration(report.Duration);
            return record != null ? text + separator + record.When() : text;
        }

        /// <summary>"Ping 22 ms · jitter 3 ms · perda 0,4% · rede estável"</summary>
        internal static string NetworkLine(NetworkReport net)
        {
            string basics = net.Basics();
            return char.ToUpper(basics[0], CultureInfo.InvariantCulture) + basics.Substring(1) + " · " + ShortVerdict(net);
        }

        /// <summary>O veredito da rede em poucas palavras (o do Raio-X é uma frase com o que fazer).</summary>
        internal static string ShortVerdict(NetworkReport net)
        {
            switch (net.Verdict)
            {
                case NetVerdict.Local:
                    return net.Wifi ? L.T("problemas no Wi-Fi", "Wi-Fi trouble") : L.T("problemas na rede de casa", "home network trouble");
                case NetVerdict.Internet:
                    return L.T("problemas depois do roteador", "trouble past the router");
                case NetVerdict.Unknown:
                    return L.T("com picos", "with spikes");
                default:
                    return net.SpikeCount == 0 ? L.T("rede estável", "network stable") : L.T("rede estável, com picos curtos", "network stable, short spikes");
            }
        }

        /// <summary>
        /// "Processador: 97% da velocidade nominal · sem freio": os números do Raio-X (CpuSpeedReport.Summary) sem o
        /// "em média a", para a linha com freio caber inteira no cartão.
        /// </summary>
        internal static string CpuLine(CpuSpeedReport cpu)
        {
            string text = L.T("Processador: ", "CPU: ") + Math.Round(cpu.AveragePerformance) + L.T("% da velocidade nominal", "% of nominal speed");
            if (!cpu.Limited) return text + L.T(" · sem freio", " · no throttling");
            return text + L.T(" · freado ", " · throttled ") + CpuSpeedReport.Span(cpu.LimitedSeconds) +
                   L.T(" (até ", " (down to ") + Math.Round(cpu.LowestLimit) + "%)";
        }

        internal static string Signature()
        {
            return L.T("Raio-X do MouseModes", "MouseModes match X-ray");
        }

        /// <summary>Rótulos da faixa de números do Raio-X (FPS médio, 1% low, travadinhas).</summary>
        internal static string[] KeyLabels()
        {
            return new[] { L.T("FPS médio", "Average FPS"), "1% low", L.T("Travadinhas", "Stutters") };
        }

        /// <summary>Os valores da faixa, arredondados como no Raio-X.</summary>
        internal static string[] KeyValues(FrameReport frames)
        {
            return new[] { Whole(frames.AverageFps), Whole(frames.Low1Fps), frames.StutterCount.ToString(CultureInfo.InvariantCulture) };
        }

        private static string Whole(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- imagem

        /// <summary>
        /// O cartão da partida como imagem: ImageWidth × scale de largura e a altura do conteúdo, desenhado num bitmap de
        /// 96 × scale dpi (as fontes em pt e as medidas × scale crescem juntas, nítido ao colar). Quem recebe libera.
        /// </summary>
        public static Bitmap Image(MatchReport report, MatchRecord record, float scale = 2f)
        {
            if (!(scale > 0)) scale = 1f;
            float dpi = 96f * scale;
            float height;
            using (var probe = new Bitmap(1, 1))
            {
                probe.SetResolution(dpi, dpi);
                using (Graphics g = Graphics.FromImage(probe))
                {
                    Theme.HighQuality(g);
                    height = Layout(g, report, record, scale, false);
                }
            }

            var bitmap = new Bitmap((int)Math.Round(ImageWidth * scale), (int)Math.Ceiling(height), PixelFormat.Format32bppArgb);
            try
            {
                bitmap.SetResolution(dpi, dpi);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    Theme.HighQuality(g);
                    g.Clear(Theme.Bg);
                    Layout(g, report, record, scale, true);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Monta o cartão de cima para baixo e devolve a altura total (px do dispositivo). Com paint = false só mede (a
        /// primeira passada, para saber o tamanho do bitmap); as duas passadas fazem as mesmas contas.
        /// </summary>
        private static float Layout(Graphics g, MatchReport report, MatchRecord record, float k, bool paint)
        {
            float cardX = Margin * k, cardY = Margin * k, cardW = (ImageWidth - Margin * 2) * k;
            float x = cardX + Pad * k, w = Inner * k;
            float y = cardY + Pad * k;

            // O fundo do cartão vai primeiro, mas a altura só se sabe no fim: desenha num caminho à parte e compõe depois.
            var paints = new List<Action>();

            // Título: o jogo e a duração na fonte de títulos (como o Raio-X); a data embaixo.
            Font titleFont = Theme.Title(16f);
            string title = Header(report, null, "  ·  ");
            y = Paragraph(g, paints, paint, title, titleFont, Theme.Text, x, y, w);
            if (record != null)
                y = Paragraph(g, paints, paint, record.When(), Theme.Ui(9f), Theme.TextSecondary, x, y, w);

            // Os números-chave, como a faixa do alto do Raio-X (Surface2 por estar sobre o cartão).
            FrameReport frames = report.Frames;
            if (frames != null)
            {
                y += 12 * k;
                var area = new RectangleF(x, y, w, ChipsHeight * k);
                string[] labels = KeyLabels(), values = KeyValues(frames);
                if (paint) paints.Add(() => Theme.DrawChips(g, area, labels, values, Theme.PerfB, Theme.Surface2, 16f, k));
                y = area.Bottom;
            }

            // A partida no tempo: o mesmo desenho do cartão do Raio-X (sem o ponto sob o mouse) e a legenda.
            MatchTimeline timeline = report.Timeline;
            if (timeline != null && timeline.HasData)
            {
                y += Gap * k;
                Font headerFont = Theme.UiSemibold(Theme.TypeScale.Group);
                Font noteFont = Theme.Ui(Theme.TypeScale.Caption);
                float headerH = (float)Math.Ceiling(headerFont.GetHeight(g));
                var headerRect = new RectangleF(x, y, w, headerH + 2 * k);
                string header = L.T("A partida no tempo", "The match over time");
                string note = L.T("um ponto a cada ", "one point every ") + TimelineChart.Seconds(timeline.BucketSeconds);
                float headerLead = Lead(g, headerFont);
                var headerTextRect = new RectangleF(headerRect.X - headerLead, headerRect.Y, headerRect.Width + headerLead, headerRect.Height);
                if (paint)
                    paints.Add(() =>
                    {
                        Theme.DrawText(g, header, headerFont, Theme.Text, headerTextRect, StringAlignment.Near, StringAlignment.Center, false);
                        Theme.DrawText(g, note, noteFont, Theme.TextSecondary, headerRect, StringAlignment.Far, StringAlignment.Center, false);
                    });
                y = headerRect.Bottom + 8 * k;

                var chartRect = new RectangleF(x, y, w, TimelineChart.PreferredHeight * k);
                if (paint)
                    paints.Add(() =>
                    {
                        using (var chart = new TimelineChart())
                        {
                            chart.SetData(timeline, report.Duration.TotalSeconds);
                            chart.Draw(g, chartRect, k, false);
                        }
                    });
                y = chartRect.Bottom + 6 * k;

                List<LegendItem> items = MatchReportForm.LegendFor(timeline);
                var legendRect = new RectangleF(x, y, w, ChartLegend.HeightFor(items, Inner) * k);
                if (paint) paints.Add(() => ChartLegend.Draw(g, items, legendRect, k));
                y = legendRect.Bottom;
            }

            // Rede e processador: uma linha cada, com o ponto verde (tudo certo) ou âmbar.
            bool rows = false;
            if (report.Network != null)
            {
                y += (rows ? 6 : Gap) * k;
                y = Row(g, paints, paint, NetworkLine(report.Network), report.Network.Verdict == NetVerdict.Stable, x, y, w, k);
                rows = true;
            }
            if (report.CpuSpeed != null)
            {
                y += (rows ? 6 : Gap) * k;
                y = Row(g, paints, paint, CpuLine(report.CpuSpeed), !report.CpuSpeed.Limited, x, y, w, k);
            }

            // Rodapé discreto: uma divisória, o logo do app e o nome.
            y += Gap * k;
            // Em pixel inteiro: a divisória sai numa linha só em Border, e o rodapé e a borda de baixo do cartão também.
            y = (float)Math.Round(y);
            float lineY = y;
            if (paint)
                paints.Add(() =>
                {
                    using (var divider = new SolidBrush(Theme.Border)) g.FillRectangle(divider, x, lineY, w, Math.Max(1, k));
                });
            y += 12 * k;
            Font footFont = Theme.Ui(Theme.TypeScale.Caption);
            float footH = Math.Max(LogoSize * k, (float)Math.Ceiling(footFont.GetHeight(g)));
            var logo = new RectangleF(x, y + (footH - LogoSize * k) / 2, LogoSize * k, LogoSize * k);
            var footRect = new RectangleF(logo.Right + 8 * k, y, w - LogoSize * k - 8 * k, footH);
            string signature = Signature();
            if (paint)
                paints.Add(() =>
                {
                    DrawLogo(g, logo, k);
                    Theme.DrawText(g, signature, footFont, Theme.TextTertiary, footRect, StringAlignment.Near, StringAlignment.Center, false);
                });
            y += footH + Pad * k;

            if (paint)
            {
                // O cartão (como o Card do Raio-X: Surface, brilho de 1 px no alto e borda), depois o conteúdo por cima.
                var card = new RectangleF(cardX + 0.5f * k, cardY + 0.5f * k, cardW - k, y - cardY - k);
                float radius = Theme.Radius.Lg * k;
                Theme.FillRounded(g, card, radius, Theme.Surface);
                using (var hl = new SolidBrush(Theme.Alpha(Color.White, 6)))
                    g.FillRectangle(hl, card.X + radius, card.Y + k, Math.Max(1, card.Width - radius * 2), k);
                Theme.StrokeRounded(g, card, radius, Theme.Border, k);
                foreach (Action draw in paints) draw();
            }
            return y + Margin * k;
        }

        /// <summary>Texto quebrado nos espaços na largura w; devolve o y depois dele.</summary>
        private static float Paragraph(Graphics g, List<Action> paints, bool paint, string text, Font font, Color color, float x, float y, float w)
        {
            List<string> lines = Theme.WrapLines(g, text, font, w);
            float lineH = font.GetHeight(g);
            // O DrawString comum põe um respiro à esquerda (~1/6 de em); recuar por ele alinha o texto às faixas e ao logo.
            float lead = Lead(g, font);
            var r = new RectangleF(x - lead, y, w + lead, lineH * lines.Count + 2);
            if (paint) paints.Add(() => Theme.DrawWrapped(g, text, font, color, r));
            // Arredondado para cima: o que vem embaixo (faixa, divisória, borda do cartão) cai em pixel inteiro.
            return (float)Math.Ceiling(y + lineH * lines.Count);
        }

        /// <summary>O respiro que o GDI+ põe à esquerda do texto num DrawString com StringFormat comum (px do dispositivo).</summary>
        private static float Lead(Graphics g, Font font)
        {
            return font.SizeInPoints * g.DpiX / 72f / 6f;
        }

        /// <summary>Linha de rede ou de processador: o ponto do veredito e o texto (quebrado se preciso). Devolve o y seguinte.</summary>
        private static float Row(Graphics g, List<Action> paints, bool paint, string text, bool good, float x, float y, float w, float k)
        {
            Font font = Theme.Ui(Theme.TypeScale.Body);
            float lineH = font.GetHeight(g);
            float textX = x + (Dot + 10) * k;
            // A altura da linha inclui descida e entrelinha: o ponto desce 10% dela para ficar no meio do corpo do texto.
            var dot = new RectangleF(x, y + (lineH - Dot * k) / 2 + 0.1f * lineH, Dot * k, Dot * k);
            if (paint)
                paints.Add(() =>
                {
                    using (var b = new SolidBrush(good ? Theme.Success : Theme.Warning)) g.FillEllipse(b, dot);
                });
            return Paragraph(g, paints, paint, text, font, Theme.Text, textX, y, w - (textX - x));
        }

        /// <summary>O logo da barra de título (mouse branco no gradiente do Performance), em tamanho de rodapé.</summary>
        private static void DrawLogo(Graphics g, RectangleF r, float k)
        {
            ModePalette p = Theme.PaletteFor(Theme.PerfA);
            using (LinearGradientBrush brush = Theme.Gradient(r, p.SigA, p.SigB, 45f))
                Theme.FillRounded(g, r, (Theme.Radius.Sm - 1) * k, brush);
            using (var white = new SolidBrush(Color.White))
                Theme.DrawGlyph(g, GlyphKind.Mouse, new RectangleF(r.X + r.Width * 0.25f, r.Y + r.Height * 0.2f, r.Width * 0.5f, r.Height * 0.6f), white, 1.1f * k);
        }

        // ---------------------------------------------------------------- área de transferência

        /// <summary>
        /// Copia a partida: o texto (UnicodeText), a imagem (Bitmap) e a mesma imagem em PNG, para cada app colar o que
        /// preferir. Devolve false se a área de transferência não aceitou (outro programa com ela aberta). Se a imagem
        /// falhar, copia só o texto.
        /// </summary>
        internal static bool Copy(MatchReport report, MatchRecord record, IClipboardTarget target)
        {
            return target.Set(Data(report, record));
        }

        /// <summary>O pacote que vai para a área de transferência (separado do Copy para os testes olharem dentro).</summary>
        internal static DataObject Data(MatchReport report, MatchRecord record)
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, true, Text(report, record));
            try
            {
                // O DataObject segura o bitmap; com copy = true o Windows já o renderiza na hora (SystemClipboardTarget).
                Bitmap image = Image(report, record);
                // Sem autoConvert: só o CF_BITMAP (o Windows faz o DIB dele), sem o blob serializado que nenhum app usa.
                data.SetData(DataFormats.Bitmap, false, image);
                var png = new MemoryStream();
                image.Save(png, ImageFormat.Png);
                png.Position = 0;
                data.SetData(PngFormat, false, png);
            }
            catch (Exception ex) when (ex is OutOfMemoryException || ex is ExternalException || ex is ArgumentException || ex is InvalidOperationException)
            {
                // GDI+ sem memória ou bitmap grande demais: o texto ainda vai.
                Log.Info(L.T("Compartilhar: imagem da partida falhou, só o texto foi copiado (", "Share: match image failed, only the text was copied (") + ex.Message + ")");
            }
            return data;
        }
    }

    /// <summary>Para onde vai o que se copia: a área de transferência do Windows, ou uma falsa nos testes.</summary>
    internal interface IClipboardTarget
    {
        /// <summary>Põe os dados; false se não deu (área de transferência ocupada por outro programa).</summary>
        bool Set(DataObject data);
    }

    /// <summary>A área de transferência do Windows (precisa da thread da interface, STA).</summary>
    internal sealed class SystemClipboardTarget : IClipboardTarget
    {
        public static readonly SystemClipboardTarget Instance = new SystemClipboardTarget();

        private SystemClipboardTarget()
        {
        }

        public bool Set(DataObject data)
        {
            try
            {
                // copy = true: os dados ficam depois de a janela fechar; 10 tentativas a cada 100 ms se outro app a segura.
                Clipboard.SetDataObject(data, true, 10, 100);
                // Com copy = true o Windows já renderizou tudo em memória dele: o bitmap (~4 MB nativos a 2x) e o PNG não
                // servem mais e são liberados já, sem esperar o finalizador. Só aqui: se o Set falhar, a área de
                // transferência ainda pode pedir os dados depois.
                var bitmap = data.GetData(DataFormats.Bitmap, false) as IDisposable;
                if (bitmap != null) bitmap.Dispose();
                var png = data.GetData(MatchShare.PngFormat, false) as IDisposable;
                if (png != null) png.Dispose();
                return true;
            }
            catch (ExternalException ex)
            {
                Log.Info(L.T("Compartilhar: a área de transferência não aceitou (", "Share: the clipboard refused (") + ex.Message + ")");
                return false;
            }
            catch (ThreadStateException)
            {
                return false;
            }
        }
    }
}
