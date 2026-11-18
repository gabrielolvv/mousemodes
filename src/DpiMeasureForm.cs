using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MouseModes
{
    /// <summary>
    /// DPI real do mouse: com uma régua na mesa, a pessoa segura o botão esquerdo no quadro, arrasta o mouse em linha
    /// reta pela distância escolhida e solta. As contagens vêm do Raw Input (as mesmas que os jogos leem).
    /// </summary>
    public class DpiMeasureForm : GamerWindow
    {
        private const int MaxRuns = 10;

        private readonly AppConfig cfg;
        private readonly Action save;
        private readonly TextBox dpiBox;
        private readonly TextBox distanceBox;
        private readonly BigReadout readout;
        private readonly ChipRow chips;
        private readonly DragPad pad;
        private readonly Label status;
        private readonly StatusRow rawWarning;
        private readonly RawMouse raw = new RawMouse();
        private readonly List<double> runs = new List<double>();
        private readonly CultureInfo culture = L.IsEnglish ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-BR");
        private bool measuring;
        private long sumX, sumY;

        /// <param name="save">Grava o config (o DPI real medido vai para o conversor de sensibilidade).</param>
        public DpiMeasureForm(AppConfig cfg, Action save) : base(L.T("DPI real do mouse", "Real mouse DPI"), true)
        {
            this.cfg = cfg;
            this.save = save;
            CloseOnEscape = true;
            TitleBar.AccentA = Theme.PerfA;
            TitleBar.AccentB = Theme.PerfB;

            Label intro = MeasuredNote(L.T("Ponha uma régua na mesa. Segure o botão esquerdo no quadro abaixo, arraste o mouse em linha reta " +
                                           "pela distância e solte. Faça umas 3 vezes, sem levantar o mouse.",
                                           "Put a ruler on the desk. Hold the left button on the box below, drag the mouse in a straight line " +
                                           "over the distance and let go. Do it about 3 times, without lifting the mouse."),
                                       20, 56, 440, Theme.TextSecondary, 9f);
            Controls.Add(intro);

            // Dois campos lado a lado e a dica numa linha embaixo deles, na largura da coluna.
            int fieldsY = intro.Bottom + 12;
            dpiBox = AddField(L.T("DPI do mouse", "Mouse DPI"), 20, fieldsY, 5);
            distanceBox = AddField(L.IsEnglish ? "Distance (in)" : "Distância (cm)", 20 + FieldWidth + 12, fieldsY, 5);
            dpiBox.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            dpiBox.Text = (cfg.AimTestDpi > 0 ? cfg.AimTestDpi : 800).ToString();
            distanceBox.Text = L.IsEnglish ? "4" : "10";
            Label fieldHint = MeasuredNote(L.T("O DPI que está no programa do mouse e o quanto você vai arrastar na régua.",
                                               "The DPI set in your mouse software and how far you will drag along the ruler."),
                                           20, fieldsY + 22 + 36 + 6, 440, Theme.TextSecondary, 8.5f);
            Controls.Add(fieldHint);
            dpiBox.TextChanged += (s, e) => Reset();
            distanceBox.TextChanged += (s, e) => Reset();

            readout = new BigReadout { Location = new Point(12, fieldHint.Bottom + 4), Size = new Size(456, 146), Unit = "DPI", AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            int y = readout.Bottom + 4;
            rawWarning = new StatusRow { Location = new Point(20, y), Size = new Size(440, 56), Glyph = GlyphKind.Mouse, Warning = true, Visible = false, Title = L.T("Raw Input indisponível", "Raw Input unavailable") };
            chips = new ChipRow
            {
                Location = new Point(20, y),
                Size = new Size(440, 52),
                Labels = new[] { L.T("Medições", "Runs"), L.T("Última", "Last"), L.T("Variação", "Spread"), L.T("Diferença", "Off by") }
            };
            pad = new DragPad { Location = new Point(20, chips.Bottom + 12), Size = new Size(440, 112) };
            pad.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Begin(); };
            pad.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) End(); };
            pad.MouseCaptureChanged += (s, e) => End();
            // Até 2 linhas: o texto mais longo é o das medições que variaram demais.
            status = MeasuredNote(L.T("As medições variaram muito e não serão guardadas: confira a distância, arraste em linha reta e não levante o mouse.",
                                      "The runs varied a lot and will not be saved: check the distance, drag in a straight line and do not lift the mouse."),
                                  20, pad.Bottom + 8, 440, Theme.TextSecondary, 8.5f, 32);

            var reset = new NeonButton { Text = L.T("Zerar", "Reset"), Location = new Point(20, status.Bottom + 12), Size = new Size(440, 36), AccentA = Theme.PerfA, AccentB = Theme.PerfB };
            reset.Click += (s, e) => Reset();
            ClientSize = new Size(480, reset.Bottom + 20);

            Controls.Add(readout);
            Controls.Add(rawWarning);
            Controls.Add(chips);
            Controls.Add(pad);
            Controls.Add(status);
            Controls.Add(reset);

            FinishLayout();
            UpdateView();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            string error;
            if (!raw.Register(this, true, OnRawInput, out error))
            {
                // Fica na tela (linha de aviso logo abaixo do número): UpdateView não apaga.
                rawWarning.Subtitle = error;
                RevealPushingDown(rawWarning, 56, 12);
                Log.Warn(L.T("DPI real: Raw Input indisponível (", "Real DPI: Raw Input unavailable (") + error + ")");
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            raw.Dispose();
            int declared = Declared();
            if (declared > 0) cfg.AimTestDpi = declared;
            // Só guarda medição confirmada: duas ou mais, sem variar demais entre si.
            if (Confirmed() && declared > 0)
            {
                // O conversor de sensibilidade mostra a volta com o DPI real enquanto o mouse estiver nesse DPI.
                cfg.MeasuredDpi = (int)Math.Round(DpiMeter.Median(runs));
                cfg.MeasuredDpiFor = declared;
                Log.Info(L.T("DPI real medido: ", "Real DPI measured: ") + cfg.MeasuredDpi + L.T(" (anunciado ", " (set to ") + declared +
                         ", " + runs.Count + L.T(" medição(ões))", " run(s))"));
            }
            if (save != null) save();
            base.OnFormClosed(e);
        }

        private void OnRawInput(int dx, int dy, ushort buttons)
        {
            if (!measuring) return;
            sumX += dx;
            sumY += dy;
            pad.Live = DpiMeter.Counts(sumX, sumY).ToString("N0", culture) + L.T(" contagens", " counts");
            pad.Invalidate();
            // O botão solto chega aqui antes do MouseUp da tela: termina já, sem somar o que vier depois.
            if ((buttons & RawMouse.LeftUp) != 0) End();
        }

        private bool Confirmed()
        {
            return runs.Count >= 2 && DpiMeter.Spread(runs) <= DpiMeter.MaxSpread;
        }

        private const int FieldWidth = 214;

        /// <summary>Rótulo do campo (sentence case) e, 6 px abaixo, a caixa de 36 px.</summary>
        private TextBox AddField(string label, int x, int y, int maxLength)
        {
            Controls.Add(new FieldLabel { Text = label, Location = new Point(x, y), Size = new Size(FieldWidth, 16) });
            var box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.NumField(12f), TextAlign = HorizontalAlignment.Center, MaxLength = maxLength };
            Controls.Add(new FieldHost(box) { Location = new Point(x, y + 22), Size = new Size(FieldWidth, 36), Accent = Theme.PerfB });
            return box;
        }

        private int Declared()
        {
            int dpi;
            return int.TryParse(dpiBox.Text, out dpi) && dpi > 0 ? dpi : 0;
        }

        private void Begin()
        {
            double distance;
            if (!Sensitivity.TryParse(distanceBox.Text, out distance))
            {
                status.Text = L.T("Informe a distância que você vai arrastar na régua.", "Enter the distance you will drag along the ruler.");
                return;
            }
            sumX = 0;
            sumY = 0;
            measuring = true;
            pad.Active = true;
            pad.Live = L.T("arraste até o fim da distância e solte", "drag to the end of the distance and let go");
            pad.Invalidate();
        }

        private void End()
        {
            if (!measuring) return;
            measuring = false;
            pad.Active = false;
            pad.Live = "";
            pad.Invalidate();

            double distance;
            if (!Sensitivity.TryParse(distanceBox.Text, out distance)) return;
            if (DpiMeter.Counts(sumX, sumY) < DpiMeter.MinCounts)
            {
                status.Text = L.T("Arrasto curto demais: segure o botão e arraste a distância toda da régua.",
                                  "Drag too short: hold the button and drag the whole distance on the ruler.");
                return;
            }
            runs.Add(DpiMeter.Dpi(sumX, sumY, DpiMeter.Inches(distance, L.IsEnglish)));
            if (runs.Count > MaxRuns) runs.RemoveAt(0);
            UpdateView();
        }

        private void Reset()
        {
            runs.Clear();
            measuring = false;
            pad.Active = false;
            pad.Live = "";
            pad.Invalidate();
            UpdateView();
        }

        private void UpdateView()
        {
            int declared = Declared();
            if (runs.Count == 0)
            {
                readout.Value = "—";
                readout.Caption = L.T("segure no quadro abaixo e arraste pela régua", "hold on the box below and drag along the ruler");
                chips.Values = new[] { "0", "—", "—", "—" };
                status.Text = L.T("O DPI anunciado é o do programa do mouse; o real é o que o sensor entrega. A velocidade do Windows não entra.",
                                  "The set DPI is the one in your mouse software; the real one is what the sensor delivers. The Windows speed does not count.");
            }
            else
            {
                double median = DpiMeter.Median(runs);
                double spread = DpiMeter.Spread(runs);
                readout.Value = Math.Round(median).ToString("0", culture);
                chips.Values = new[]
                {
                    runs.Count.ToString(),
                    Math.Round(runs[runs.Count - 1]).ToString("0", culture),
                    runs.Count < 2 ? "—" : (spread * 100).ToString("0.0", culture) + "%",
                    declared > 0 ? Percent(DpiMeter.Deviation(median, declared)) : "—"
                };
                readout.Caption = declared > 0
                    ? L.T("anunciado ", "set to ") + declared + " · " + Gap(DpiMeter.Deviation(median, declared))
                    : L.T("informe o DPI do programa do mouse para comparar", "enter the DPI from your mouse software to compare");
                if (runs.Count < 2)
                    status.Text = L.T("Faça mais uma medição para confirmar: com uma só, o valor não é guardado.",
                                      "Do one more run to confirm: with only one, the value is not saved.");
                else if (spread > DpiMeter.MaxSpread)
                    status.Text = L.T("As medições variaram muito e não serão guardadas: confira a distância, arraste em linha reta e não levante o mouse.",
                                      "The runs varied a lot and will not be saved: check the distance, drag in a straight line and do not lift the mouse.");
                else
                    status.Text = L.T("Pronto. Ao fechar, o conversor de sensibilidade passa a mostrar a volta com o DPI real.",
                                      "Done. When you close this, the sensitivity converter shows the turn with the real DPI.");
            }
            readout.Invalidate();
            chips.Invalidate();
        }

        private string Percent(double deviation)
        {
            return (deviation > 0 ? "+" : "") + (deviation * 100).ToString("0.0", culture) + "%";
        }

        /// <summary>"4,6% a menos que o anunciado" / "igual ao anunciado".</summary>
        private string Gap(double deviation)
        {
            double pct = Math.Abs(deviation * 100);
            if (pct < 0.5) return L.T("igual ao anunciado", "same as set");
            return pct.ToString("0.0", culture) + (deviation < 0 ? L.T("% a menos", "% lower") : L.T("% a mais", "% higher"));
        }
    }

    /// <summary>O quadro onde a pessoa segura o botão para começar a medir.</summary>
    public class DragPad : GControl
    {
        public bool Active { get; set; }
        public string Live { get; set; }

        public DragPad()
        {
            Live = "";
            Cursor = Cursors.SizeAll;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);
            float k = g.DpiX / 96f;
            ModePalette p = Theme.PerformancePalette;
            var card = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, card, Theme.Radius.Md, Active ? Theme.Lerp(Theme.Surface, p.Fill, 0.08f) : Theme.Surface);
            using (var pen = new Pen(Active ? p.Fill : Theme.BorderStrong, Active ? 1.5f : 1f) { DashPattern = new[] { 4f, 3f } })
            using (GraphicsPath path = Theme.Rounded(card, Theme.Radius.Md))
                g.DrawPath(pen, path);

            // Ícone, título em sentence case e a dica (ou a contagem ao vivo), centralizados juntos.
            Font titleFont = Theme.UiSemibold(11f), hintFont = Theme.Ui(9f);
            float icon = 20 * k, titleH = titleFont.GetHeight(g), hintH = hintFont.GetHeight(g);
            float top = (Height - icon - 8 * k - titleH - 2 * k - hintH) / 2f;
            using (var ib = new SolidBrush(Active ? p.OnSurface : Theme.TextTertiary))
                Theme.DrawGlyph(g, GlyphKind.Mouse, new RectangleF((Width - icon) / 2f, top, icon, icon), ib, 1.6f * k);
            top += icon + 8 * k;
            Theme.DrawText(g, Active ? L.T("Medindo...", "Measuring...") : L.T("Segure aqui e arraste", "Hold here and drag"),
                titleFont, Active ? p.OnSurface : Theme.Text, new RectangleF(0, top, Width, titleH + 2), StringAlignment.Center, StringAlignment.Near, false);
            top += titleH + 2 * k;
            Theme.DrawText(g, Live.Length > 0 ? Live : L.T("o mouse pode sair do quadro: o que conta é a régua",
                                                           "the mouse may leave the box: the ruler is what counts"),
                hintFont, Theme.TextSecondary, new RectangleF(0, top, Width, hintH + 2), StringAlignment.Center, StringAlignment.Near, false);
        }
    }
}
