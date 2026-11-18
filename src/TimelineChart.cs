using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// A partida no tempo, dentro de um cartão do Raio-X: FPS médio e 1% low de cada balde, as travadinhas, os trechos de
    /// processador freado e, embaixo, o ping. Passar o mouse mostra os números do ponto. Sem moldura própria (o cartão é
    /// a moldura); tudo que é medida passa por k = DpiX / 96.
    /// </summary>
    public class TimelineChart : GControl
    {
        /// <summary>Altura no cartão (px a 96 dpi).</summary>
        public const int PreferredHeight = 150;

        // Medidas a 96 dpi: coluna dos valores à esquerda, respiro no alto, eixo de tempo, faixa do ping e o vão entre as faixas.
        internal const float Gutter = 36, GutterGap = 6, RightPad = 4, TopPad = 8, AxisHeight = 16, AxisGap = 4, PingHeight = 28, BandGap = 18;
        /// <summary>Faixas de FPS com no máximo 3 divisões (2 a 3 valores na coluna da esquerda).</summary>
        internal const int FpsSteps = 3;
        /// <summary>Espaço mínimo entre dois rótulos de tempo (px a 96 dpi).</summary>
        internal const float TimeLabelSpace = 48;

        /// <summary>Alfa das faixas de processador freado (o quadrado da legenda usa o mesmo).</summary>
        internal const int SlowdownAlpha = 28;
        /// <summary>Traço de travadinha: fino e translúcido, para muitas juntas virarem intensidade e não uma barra.</summary>
        internal const int StutterAlpha = 120;

        private static readonly double[] NiceMultipliers = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };
        private static readonly int[] MinuteSteps = { 1, 2, 5, 10, 15, 20, 30, 60, 120, 240 };

        private MatchTimeline timeline;
        private double total;
        private int hover = -1;
        // Onde o último OnPaint pôs a área do gráfico (px do dispositivo): o mouse usa a mesma conta.
        private float plotX, plotW;
        // Escalas das faixas (topo e passo da grade), calculadas em SetData.
        private double fpsTop, fpsStep, pingTop, pingStep;
        private bool hasFps, hasPing;

        public TimelineChart()
        {
            BackColor = Theme.Surface;
            TabStop = false;
            AccessibleRole = AccessibleRole.Chart;
            AccessibleName = L.T("A partida no tempo", "The match over time");
        }

        /// <summary>A série e a duração da partida (o eixo vai até a maior entre ela e o último balde).</summary>
        public void SetData(MatchTimeline data, double durationSeconds)
        {
            timeline = data;
            total = Span(data, durationSeconds);
            hover = -1;
            double maxFps = 0, maxPing = 0;
            hasFps = hasPing = false;
            if (data != null && data.Points != null)
                foreach (TimelinePoint p in data.Points)
                {
                    if (p.HasFps)
                    {
                        hasFps = true;
                        maxFps = Math.Max(maxFps, p.Fps);
                    }
                    if (p.HasPing)
                    {
                        hasPing = true;
                        maxPing = Math.Max(maxPing, p.Ping);
                    }
                }
            fpsTop = NiceTop(maxFps, FpsSteps, 30, out fpsStep);
            // Com o FPS em cima, o ping tem uma faixa baixa: só o topo e a base.
            pingTop = NiceTop(maxPing, hasFps ? 1 : 2, 10, out pingStep);
            AccessibleDescription = Describe(data, total);
            Invalidate();
        }

        /// <summary>O ponto sob o mouse (-1 = nenhum).</summary>
        internal int HoverIndex
        {
            get { return hover; }
        }

        /// <summary>Onde o último OnPaint pôs a área do gráfico (px do dispositivo); 0 antes da primeira pintura.</summary>
        internal float PlotX
        {
            get { return plotX; }
        }

        internal float PlotW
        {
            get { return plotW; }
        }

        // ---------------------------------------------------------------- contas (sem desenho, testáveis)

        /// <summary>Segundos que o eixo de tempo cobre: a duração, ou até o fim do último balde se ele passa dela.</summary>
        internal static double Span(MatchTimeline t, double durationSeconds)
        {
            double span = Math.Max(0, durationSeconds);
            if (t != null && t.Points != null && t.Points.Count > 0)
                span = Math.Max(span, t.Points[t.Points.Count - 1].At + t.BucketSeconds);
            return Math.Max(span, 1);
        }

        /// <summary>
        /// Passo "redondo" da grade (1; 1,5; 2; 2,5; 3; 4; 5; 6 ou 8 × 10ⁿ) para que o máximo caiba em no máximo steps divisões. O topo
        /// do eixo fica em Math.Ceiling(max / passo) × passo.
        /// </summary>
        internal static double NiceStep(double max, int steps)
        {
            if (!(max > 0)) max = 1;
            double target = max / Math.Max(1, steps);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
            foreach (double m in NiceMultipliers)
                if (m * magnitude >= target * (1 - 1e-9)) return m * magnitude;
            return 10 * magnitude;
        }

        /// <summary>Topo do eixo (múltiplo do passo, com uma folga de 2% para a linha não encostar no alto).</summary>
        internal static double NiceTop(double max, int steps, double floor, out double step)
        {
            double m = Math.Max(max * 1.02, floor);
            step = NiceStep(m, steps);
            return Math.Ceiling(m / step - 1e-9) * step;
        }

        /// <summary>Passo do eixo de tempo, em minutos, para caberem no máximo maxLabels rótulos.</summary>
        internal static int TimeStepMinutes(double totalSeconds, int maxLabels)
        {
            double minutes = totalSeconds / 60;
            int intervals = Math.Max(1, maxLabels - 1);
            foreach (int step in MinuteSteps)
                if (Math.Floor(minutes / step + 1e-9) <= intervals) return step;
            return MinuteSteps[MinuteSteps.Length - 1];
        }

        /// <summary>O balde sob o x do mouse (px), na área do gráfico [x, x + width]; -1 fora dela.</summary>
        internal static int IndexAt(MatchTimeline t, double totalSeconds, float x, float width, float mouseX)
        {
            if (t == null || t.Points == null || t.Points.Count == 0 || width <= 0) return -1;
            if (mouseX < x || mouseX > x + width) return -1;
            double at = (mouseX - x) / width * totalSeconds;
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < t.Points.Count; i++)
            {
                TimelinePoint p = t.Points[i];
                if (at >= p.At && at < p.At + t.BucketSeconds) return i;
                double d = Math.Abs(at - (p.At + t.BucketSeconds / 2));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>"12 min · 143 FPS · 21 ms": o minuto do balde e só o que foi medido nele.</summary>
        internal static string HoverText(TimelinePoint p, double bucketSeconds)
        {
            var parts = new List<string> { Minutes((p.At + bucketSeconds / 2) / 60) + " min" };
            if (p.HasFps) parts.Add(Whole(p.Fps) + " FPS");
            if (p.HasPing) parts.Add(p.Ping.ToString(CultureInfo.InvariantCulture) + " ms");
            if (p.Lost > 0)
                parts.Add(p.Lost == 1 ? L.T("1 ping perdido", "1 lost ping")
                                      : L.T(p.Lost + " pings perdidos", p.Lost + " lost pings"));
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>O gráfico em texto, para leitor de tela: duração, FPS médio, o pior trecho, travadinhas, freio e ping.</summary>
        internal static string Describe(MatchTimeline t, double totalSeconds)
        {
            if (t == null || t.Points == null) return "";
            var text = new List<string>();
            text.Add(L.T("Partida de ", "Match of ") + MatchReport.FormatDuration(TimeSpan.FromSeconds(totalSeconds)) + ".");

            double sum = 0;
            int count = 0;
            TimelinePoint lowest = null;
            var pings = new List<int>();
            int lost = 0;
            foreach (TimelinePoint p in t.Points)
            {
                lost += p.Lost;
                if (p.HasPing) pings.Add(p.Ping);
                if (!p.HasFps) continue;
                sum += p.Fps;
                count++;
                if (lowest == null || p.Fps < lowest.Fps) lowest = p;
            }
            if (count > 0)
                text.Add(L.T("FPS médio " + Whole(sum / count) + "; o trecho mais baixo teve " + Whole(lowest.Fps) + " FPS, aos " +
                             Minutes(lowest.At / 60) + " min.",
                             "Average FPS " + Whole(sum / count) + "; the lowest stretch had " + Whole(lowest.Fps) + " FPS, at " +
                             Minutes(lowest.At / 60) + " min."));
            int stutters = t.Stutters != null ? t.Stutters.Count : 0;
            if (count > 0)
                text.Add(stutters == 1 ? L.T("1 travadinha.", "1 stutter.")
                                       : L.T(stutters + " travadinhas.", stutters + " stutters."));
            double slow = 0;
            if (t.Slowdowns != null)
                foreach (TimelineSpan s in t.Slowdowns) slow += s.Seconds;
            if (slow > 0)
                text.Add(L.T("Processador freado por ", "CPU throttled for ") + Seconds(slow) + ".");
            if (pings.Count > 0)
            {
                pings.Sort();
                string ping = L.T("Ping mediano ", "Median ping ") + pings[pings.Count / 2].ToString(CultureInfo.InvariantCulture) + " ms";
                if (lost > 0) ping += lost == 1 ? L.T(", 1 perdido", ", 1 lost") : L.T(", " + lost + " perdidos", ", " + lost + " lost");
                text.Add(ping + ".");
            }
            return string.Join(" ", text.ToArray());
        }

        /// <summary>"45 s" abaixo de 1 min; "3 min" a partir dele.</summary>
        internal static string Seconds(double seconds)
        {
            return seconds < 60 ? Whole(seconds) + " s" : MatchReport.FormatDuration(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>
        /// Rótulos da grade, da base ao topo (o zero fica sem rótulo): no FPS a unidade só vai no do topo ("180 FPS",
        /// "120", "60"), para a faixa não se ler como a continuação da do ping; no ping vai em todos ("100 ms").
        /// </summary>
        internal static List<string> GridLabels(double top, double step, string unit, bool unitOnTopOnly)
        {
            var labels = new List<string>();
            for (double v = step; v <= top + step / 2; v += step)
            {
                bool isTop = v + step / 2 > top;
                labels.Add(Whole(v) + (isTop || !unitOnTopOnly ? unit : ""));
            }
            return labels;
        }

        private static string Whole(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        private static string Minutes(double minutes)
        {
            return ((int)Math.Floor(minutes)).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- desenho

        /// <summary>As faixas (px do dispositivo): FPS em cima, ping embaixo; a que não tem dado some e a outra fica com o espaço.</summary>
        private void Bands(float k, float gutter, bool fps, bool ping, out RectangleF fpsBand, out RectangleF pingBand, out RectangleF axis)
        {
            float x = gutter, w = Math.Max(1, Width - x - RightPad * k);
            float top = TopPad * k;
            float bottom = Height - AxisHeight * k;
            axis = new RectangleF(x, bottom, w, AxisHeight * k);
            float plotBottom = bottom - AxisGap * k;
            if (fps && ping)
            {
                pingBand = new RectangleF(x, plotBottom - PingHeight * k, w, PingHeight * k);
                fpsBand = new RectangleF(x, top, w, Math.Max(1, pingBand.Y - BandGap * k - top));
            }
            else if (fps)
            {
                pingBand = RectangleF.Empty;
                fpsBand = new RectangleF(x, top, w, Math.Max(1, plotBottom - top));
            }
            else
            {
                fpsBand = RectangleF.Empty;
                pingBand = new RectangleF(x, top, w, Math.Max(1, plotBottom - top));
            }
        }

        private float X(double seconds)
        {
            return plotX + (float)(Math.Max(0, Math.Min(total, seconds)) / total) * plotW;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            if (timeline == null || timeline.Points == null || (!hasFps && !hasPing)) return;
            float k = g.DpiX / 96f;
            Font valueFont = Theme.Num(Theme.TypeScale.Caption);
            Font axisFont = Theme.Ui(Theme.TypeScale.Caption);

            // A coluna da esquerda tem a largura do maior rótulo da grade ("180 FPS", "100 ms"): todos são medidos,
            // porque os algarismos da fonte Num são proporcionais e um valor do meio pode ser mais largo que o do topo.
            List<string> fpsLabels = hasFps ? GridLabels(fpsTop, fpsStep, FpsUnit, true) : new List<string>();
            List<string> pingLabels = hasPing ? GridLabels(pingTop, pingStep, PingUnit, false) : new List<string>();
            float gutter = 0;
            using (var format = LabelFormat())
            {
                foreach (string label in fpsLabels) gutter = Math.Max(gutter, g.MeasureString(label, valueFont, PointF.Empty, format).Width);
                foreach (string label in pingLabels) gutter = Math.Max(gutter, g.MeasureString(label, valueFont, PointF.Empty, format).Width);
            }
            gutter = Math.Max(Gutter * k / 2, (float)Math.Ceiling(gutter) + 1 * k + GutterGap * k);

            RectangleF fpsBand, pingBand, axis;
            Bands(k, gutter, hasFps, hasPing, out fpsBand, out pingBand, out axis);
            plotX = axis.X;
            plotW = axis.Width;
            float half = (float)(timeline.BucketSeconds / 2);

            // Freio do processador: faixas de fundo na faixa principal, por baixo de tudo.
            RectangleF main = hasFps ? fpsBand : pingBand;
            if (timeline.Slowdowns != null)
                using (var slow = new SolidBrush(Theme.Alpha(Theme.Warning, SlowdownAlpha)))
                    foreach (TimelineSpan s in timeline.Slowdowns)
                    {
                        float x1 = X(s.At), x2 = X(s.At + s.Seconds);
                        g.FillRectangle(slow, x1, main.Y, Math.Max(2 * k, x2 - x1), main.Height);
                    }

            if (hasFps)
            {
                Grid(g, fpsBand, fpsTop, fpsStep, valueFont, fpsLabels);

                // Travadinhas: traços curtos, finos e translúcidos na base da faixa de FPS, por baixo da linha e da área do
                // FPS (muitas juntas viram intensidade da cor, sem esconder o FPS baixo).
                if (timeline.Stutters != null)
                    using (var tick = new Pen(Theme.Alpha(Theme.Warning, StutterAlpha), 1 * k))
                        foreach (double at in timeline.Stutters)
                        {
                            float x = X(at);
                            g.DrawLine(tick, x, fpsBand.Bottom - 1 * k, x, fpsBand.Bottom - 8 * k);
                        }

                DrawFps(g, fpsBand, fpsTop, half, k);
            }

            if (hasPing)
            {
                Grid(g, pingBand, pingTop, pingStep, valueFont, pingLabels);
                DrawPing(g, pingBand, pingTop, half, k);
            }

            DrawAxis(g, axis, axisFont, k);
            if (hover >= 0 && hover < timeline.Points.Count) DrawHover(g, timeline.Points[hover], main, fpsBand, pingBand, k);
        }

        private const string FpsUnit = " FPS", PingUnit = " ms";

        /// <summary>Rótulo da grade: uma linha, sem reticências (a coluna da esquerda já foi medida com todos).</summary>
        private static StringFormat LabelFormat()
        {
            return new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.None };
        }

        /// <summary>Grade de 1 px nítida (FillRectangle) da base ao topo, com o valor de cada linha (menos o zero) à esquerda.</summary>
        private void Grid(Graphics g, RectangleF band, double top, double step, Font font, List<string> labels)
        {
            float k = g.DpiX / 96f;
            float h = font.GetHeight(g);
            using (var grid = new SolidBrush(Theme.Decor))
            using (var text = new SolidBrush(Theme.TextTertiary))
            using (var format = LabelFormat())
            {
                format.Alignment = StringAlignment.Far;
                int i = 0;
                for (double v = 0; v <= top + step / 2; v += step)
                {
                    float y = (float)Math.Round(band.Bottom - (float)(v / top) * band.Height);
                    g.FillRectangle(grid, band.X, y, band.Width, 1);
                    if (v <= 0 || i >= labels.Count) continue;
                    g.DrawString(labels[i++], font, text, new RectangleF(0, y - h / 2f, band.X - GutterGap * k, h + 2 * k), format);
                }
            }
        }

        private float Y(RectangleF band, double top, double value)
        {
            return band.Bottom - (float)(Math.Max(0, Math.Min(top, value)) / top) * band.Height;
        }

        /// <summary>Linha do FPS médio e, por baixo, a faixa até o 1% low; balde sem quadros (NaN) quebra a linha.</summary>
        private void DrawFps(Graphics g, RectangleF band, double top, float half, float k)
        {
            ModePalette p = Theme.PaletteFor(Theme.PerfA);
            using (var area = new SolidBrush(Theme.Alpha(p.Fill, 46)))
            using (var line = new Pen(p.Fill, 1.5f * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (List<TimelinePoint> run in Runs(q => q.HasFps))
                {
                    var upper = new List<PointF>();
                    var lower = new List<PointF>();
                    foreach (TimelinePoint t in run)
                    {
                        float low = float.IsNaN(t.Low1Fps) ? t.Fps : Math.Min(t.Low1Fps, t.Fps);
                        foreach (float x in Xs(t, run.Count, half))
                        {
                            upper.Add(new PointF(x, Y(band, top, t.Fps)));
                            lower.Add(new PointF(x, Y(band, top, low)));
                        }
                    }
                    lower.Reverse();
                    var polygon = new List<PointF>(upper);
                    polygon.AddRange(lower);
                    g.FillPolygon(area, polygon.ToArray());
                    g.DrawLines(line, upper.ToArray());
                }
            }
        }

        /// <summary>Linha do ping (cor do Dia a dia, para não confundir com o FPS) e as perdas em vermelho no alto da faixa.</summary>
        private void DrawPing(Graphics g, RectangleF band, double top, float half, float k)
        {
            using (var line = new Pen(Theme.DailyPalette.OnSurface, 1.5f * k) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                foreach (List<TimelinePoint> run in Runs(q => q.HasPing))
                {
                    var pts = new List<PointF>();
                    foreach (TimelinePoint t in run)
                        foreach (float x in Xs(t, run.Count, half))
                            pts.Add(new PointF(x, Y(band, top, t.Ping)));
                    g.DrawLines(line, pts.ToArray());
                }
            using (var lost = new SolidBrush(Theme.Danger))
                foreach (TimelinePoint t in timeline.Points)
                {
                    if (t.Lost <= 0) continue;
                    float x = X(t.At + half), d = 5 * k;
                    g.FillEllipse(lost, x - d / 2, band.Bottom - d - 1 * k, d, d);
                }
        }

        /// <summary>Um ponto fica no meio do balde; um trecho de um balde só vira um traço da largura dele.</summary>
        private IEnumerable<float> Xs(TimelinePoint t, int runLength, float half)
        {
            if (runLength > 1)
            {
                yield return X(t.At + half);
                yield break;
            }
            yield return X(t.At);
            yield return X(t.At + 2 * half);
        }

        /// <summary>Trechos seguidos de baldes que passam no filtro (para quebrar a linha nas lacunas).</summary>
        private List<List<TimelinePoint>> Runs(Predicate<TimelinePoint> has)
        {
            var runs = new List<List<TimelinePoint>>();
            List<TimelinePoint> current = null;
            TimelinePoint previous = null;
            foreach (TimelinePoint t in timeline.Points)
            {
                bool contiguous = previous != null && t.At - previous.At <= timeline.BucketSeconds * 1.5;
                if (!has(t))
                {
                    current = null;
                }
                else
                {
                    if (current == null || !contiguous)
                    {
                        current = new List<TimelinePoint>();
                        runs.Add(current);
                    }
                    current.Add(t);
                }
                previous = t;
            }
            return runs;
        }

        /// <summary>Minutos no eixo de tempo: "0  10  20  30 min", sem passar das bordas do controle.</summary>
        private void DrawAxis(Graphics g, RectangleF axis, Font font, float k)
        {
            int maxLabels = Math.Max(2, (int)(axis.Width / (TimeLabelSpace * k)) + 1);
            int step = TimeStepMinutes(total, maxLabels);
            int last = (int)Math.Floor(total / 60 / step + 1e-9);
            using (var brush = new SolidBrush(Theme.TextTertiary))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                for (int i = 0; i <= last; i++)
                {
                    int minutes = i * step;
                    string text = minutes.ToString(CultureInfo.InvariantCulture) + (i == last ? " min" : "");
                    float w = g.MeasureString(text, font, PointF.Empty, format).Width;
                    float x = X(minutes * 60.0) - w / 2;
                    x = Math.Max(0, Math.Min(Width - w, x));
                    g.DrawString(text, font, brush, x, axis.Y + 1 * k, format);
                }
            }
        }

        /// <summary>Linha vertical no balde sob o mouse, os pontos nas linhas e um rótulo pequeno que não sai do controle.</summary>
        private void DrawHover(Graphics g, TimelinePoint t, RectangleF main, RectangleF fpsBand, RectangleF pingBand, float k)
        {
            float half = (float)(timeline.BucketSeconds / 2);
            float x = X(t.At + half);
            float top = main.Y, bottom = pingBand.IsEmpty ? main.Bottom : pingBand.Bottom;
            using (var guide = new SolidBrush(Theme.Alpha(Color.White, 70)))
                g.FillRectangle(guide, (float)Math.Round(x), top, 1, bottom - top);

            float d = 7 * k;
            if (t.HasFps && !fpsBand.IsEmpty) Dot(g, x, Y(fpsBand, fpsTop, t.Fps), d, k, Theme.PaletteFor(Theme.PerfA).Fill);
            if (t.HasPing && !pingBand.IsEmpty) Dot(g, x, Y(pingBand, pingTop, t.Ping), d, k, Theme.DailyPalette.OnSurface);

            string text = HoverText(t, timeline.BucketSeconds);
            Font font = Theme.UiSemibold(Theme.TypeScale.Caption);
            SizeF size;
            using (var format = new StringFormat(StringFormatFlags.NoWrap)) size = g.MeasureString(text, font, PointF.Empty, format);
            float bw = size.Width + 12 * k, bh = size.Height + 6 * k;
            float bx = x + 8 * k;
            if (bx + bw > Width - 1) bx = x - 8 * k - bw;
            bx = Math.Max(0, Math.Min(Width - 1 - bw, bx));
            var box = new RectangleF(bx, top, bw, bh);
            Theme.FillRounded(g, box, 4 * k, Theme.Surface2);
            Theme.StrokeRounded(g, box, 4 * k, Theme.BorderStrong, 1f);
            Theme.DrawText(g, text, font, Theme.Text, box, StringAlignment.Center, StringAlignment.Center, false);
        }

        private static void Dot(Graphics g, float x, float y, float d, float k, Color color)
        {
            using (var ring = new SolidBrush(Theme.Surface)) g.FillEllipse(ring, x - d / 2 - 1.5f * k, y - d / 2 - 1.5f * k, d + 3 * k, d + 3 * k);
            using (var fill = new SolidBrush(color)) g.FillEllipse(fill, x - d / 2, y - d / 2, d, d);
        }

        // ---------------------------------------------------------------- mouse

        // O hover do gráfico é o próprio (campo hover); sem isso o GControl animaria HoverT e redesenharia tudo à toa.
        protected override bool Interactive { get { return false; } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (plotW <= 0)
            {
                float k = DeviceDpi / 96f;
                plotX = Gutter * k;
                plotW = Math.Max(1, Width - plotX - RightPad * k);
            }
            int index = IndexAt(timeline, total, plotX, plotW, e.X);
            if (index == hover) return;
            hover = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover < 0) return;
            hover = -1;
            Invalidate();
        }
    }

    /// <summary>Um item da legenda do gráfico: a marca (ponto, ou quadrado para um trecho) na cor da série e o nome.</summary>
    public class LegendItem
    {
        public string Text;
        public Color Color;
        public bool Square;
        /// <summary>Contorno da marca quadrada (Color.Empty = sem contorno).</summary>
        public Color Outline;

        public LegendItem(string text, Color color, bool square = false)
        {
            Text = text;
            Color = color;
            Square = square;
        }
    }

    /// <summary>Legenda curta de gráfico: marcas coloridas e nomes, quebrando em mais de uma linha só quando não cabe.</summary>
    public class ChartLegend : GControl
    {
        /// <summary>Altura de uma linha (px a 96 dpi).</summary>
        public const int LineHeight = 18;
        // Medidas a 96 dpi: a marca, o vão até o nome e o vão até o próximo item.
        private const float Mark = 7, MarkGap = 5, ItemGap = 12;
        private const float FontSize = Theme.TypeScale.Caption;

        private readonly List<LegendItem> items = new List<LegendItem>();

        public ChartLegend()
        {
            BackColor = Theme.Surface;
            TabStop = false;
        }

        protected override bool Interactive { get { return false; } }

        public IList<LegendItem> Items
        {
            get { return items.AsReadOnly(); }
        }

        public void SetItems(IEnumerable<LegendItem> list)
        {
            items.Clear();
            items.AddRange(list);
            var names = new List<string>();
            foreach (LegendItem item in items) names.Add(item.Text);
            Text = string.Join(", ", names.ToArray());
            AccessibleName = L.T("Legenda: ", "Legend: ") + Text;
            Invalidate();
        }

        /// <summary>Largura de cada item (marca, vão e nome) em px a 96 dpi, medida na fonte da legenda.</summary>
        internal static float[] ItemWidths(IList<LegendItem> list)
        {
            var widths = new float[list.Count];
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                using (var format = new StringFormat(StringFormatFlags.NoWrap))
                {
                    float k = g.DpiX / 96f;
                    for (int i = 0; i < list.Count; i++)
                        widths[i] = Mark + MarkGap + (float)Math.Ceiling(g.MeasureString(list[i].Text, Theme.Ui(FontSize), PointF.Empty, format).Width / k);
                }
            }
            catch
            {
                for (int i = 0; i < list.Count; i++) widths[i] = Mark + MarkGap + list[i].Text.Length * 6;
            }
            return widths;
        }

        /// <summary>Em que linha fica cada item (na largura dada, px a 96 dpi): um item nunca é cortado ao meio.</summary>
        internal static int[] RowsFor(float[] widths, float width)
        {
            var rows = new int[widths.Length];
            int row = 0;
            float x = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                if (x > 0 && x + widths[i] > width)
                {
                    row++;
                    x = 0;
                }
                rows[i] = row;
                x += widths[i] + ItemGap;
            }
            return rows;
        }

        /// <summary>Altura (px a 96 dpi) da legenda com esses itens nessa largura.</summary>
        public static int HeightFor(IList<LegendItem> list, int width)
        {
            if (list.Count == 0) return 0;
            int[] rows = RowsFor(ItemWidths(list), width);
            return (rows[rows.Length - 1] + 1) * LineHeight;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            if (items.Count == 0) return;
            float k = g.DpiX / 96f;
            Font font = Theme.Ui(FontSize);
            int[] rows = RowsFor(ItemWidths(items), Width / k);
            float lineH = LineHeight * k, mark = Mark * k;
            float x = 0;
            int row = 0;
            using (var brush = new SolidBrush(Theme.TextSecondary))
            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (rows[i] != row)
                    {
                        row = rows[i];
                        x = 0;
                    }
                    LegendItem item = items[i];
                    float top = row * lineH;
                    var markRect = new RectangleF(x, top + (lineH - mark) / 2, mark, mark);
                    if (item.Square)
                    {
                        Theme.FillRounded(g, markRect, 1.5f * k, item.Color);
                        // A cor de um trecho é translúcida (como no gráfico): o contorno deixa a marca visível.
                        if (item.Outline.A > 0) Theme.StrokeRounded(g, markRect, 1.5f * k, item.Outline, 1f);
                    }
                    else using (var b = new SolidBrush(item.Color)) g.FillEllipse(b, markRect);
                    x += mark + MarkGap * k;
                    float w = g.MeasureString(item.Text, font, PointF.Empty, format).Width;
                    float h = font.GetHeight(g);
                    g.DrawString(item.Text, font, brush, x, top + (lineH - h) / 2, format);
                    x += w + ItemGap * k;
                }
            }
        }
    }
}
